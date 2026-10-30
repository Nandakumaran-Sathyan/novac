using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using SmartPOS.Application.AI;
using SmartPOS.Application.Auth;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Persistence;

namespace SmartPOS.IntegrationTests;

/// <summary>Collects emails instead of sending them, so tests can read the verification link.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        Sent.Enqueue((to, subject, body));
        return Task.CompletedTask;
    }
}

/// <summary>No network in tests: "nomail.invalid" cannot receive mail, "unknowable.test" simulates a failed lookup, everything else can.</summary>
public sealed class FakeEmailAddressChecker : IEmailAddressChecker
{
    public Task<EmailDomainStatus> CheckDomainAsync(string domain, CancellationToken cancellationToken) =>
        Task.FromResult(domain switch
        {
            "nomail.invalid" => EmailDomainStatus.CannotReceiveMail,
            "unknowable.test" => EmailDomainStatus.Unknown,
            _ => EmailDomainStatus.CanReceiveMail
        });
}

/// <summary>Stands in for Ollama so tests never depend on a model server. Tests script its replies.</summary>
public sealed class FakeChatModelClient : IChatModelClient
{
    public ChatModelStatus Status { get; set; } = new(true, "fake-model", true, true, "test", null);
    public Queue<ChatModelResponse> Replies { get; } = new();
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    /// <summary>When set, the next call throws it (e.g. AIUnavailableException).</summary>
    public Exception? ThrowNext { get; set; }

    /// <summary>When set, calls wait for it to complete (to test concurrent requests).</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// When set, decides each reply from what the model is sent (the messages so far and the tools offered),
    /// so a test can act like a model that reads real tool results, or one that invents figures.
    /// </summary>
    public Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, ChatModelResponse>? Responder { get; set; }

    public List<int> ToolsOffered { get; } = [];

    public async Task<ChatModelResponse> CompleteAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        lock (Calls) { Calls.Add([.. messages]); ToolsOffered.Add(tools.Count); }
        if (Responder is { } responder)
            return responder(messages, tools);
        if (Hold is { } hold)
            await hold.Task.WaitAsync(cancellationToken);
        if (ThrowNext is { } ex)
        {
            ThrowNext = null;
            throw ex;
        }
        lock (Replies)
            return Replies.Count > 0
                ? Replies.Dequeue()
                : new ChatModelResponse(new ChatMessage(ChatRole.Assistant, "ok"), false, new ChatModelUsage(1, 1, TimeSpan.Zero));
    }

    public Task<ChatModelStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);
}

/// <summary>
/// Runs the real API pipeline (auth, validation, controllers, services, EF Core) in memory.
/// SQLite in-memory replaces MySQL so the tests need no database server; MySQL-specific behaviour
/// (e.g. generated SQL, locking) is covered by the manual smoke test described in docs/TESTING.md.
/// </summary>
public sealed class SmartPosApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only credentials for a throwaway in-memory database.
    public const string UserEmail = "owner@test.local";
    public const string UserPassword = "OwnerPass123";
    // A second, completely separate shop and user, used to prove tenant isolation.
    public const string OtherEmail = "other.owner@test.local";
    public const string OtherPassword = "OtherPass123";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public FakeEmailSender Mail { get; } = new();
    public FakeChatModelClient Model { get; } = new();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public int ShopId { get; private set; }
    public int OtherShopId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
        builder.UseSetting("Assistant:RequestsPerMinute", "1000"); // one dedicated test lowers it
        builder.UseSetting("Pos:TaxRatePercent", "5");
        builder.UseSetting("Pos:MaxDiscountPercent", "50");
        builder.UseSetting("App:WebBaseUrl", "https://pos.test");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SmartPosDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SmartPosDbContext>>();
            services.AddDbContext<SmartPosDbContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<IEmailSender>();
            services.RemoveAll<IEmailAddressChecker>();
            services.AddSingleton(Mail);
            services.AddSingleton<IEmailSender>(Mail);
            services.AddSingleton<IEmailAddressChecker, FakeEmailAddressChecker>();
            services.RemoveAll<IChatModelClient>();
            services.AddSingleton(Model);
            services.AddSingleton<IChatModelClient>(Model);
        });
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync(); // the in-memory database lives as long as this connection
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartPosDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var shop = new Shop { ShopName = "Test Shop", Address = "1 Test Street", CreatedAt = now };
        var otherShop = new Shop { ShopName = "Other Shop", Address = "2 Other Street", CreatedAt = now };
        db.Shops.AddRange(shop, otherShop);
        await db.SaveChangesAsync();
        ShopId = shop.Id;
        OtherShopId = otherShop.Id;

        db.Users.AddRange(
            new User { ShopId = shop.Id, FullName = "Test Owner", Email = UserEmail, PasswordHash = hasher.Hash(UserPassword), CreatedAt = now, UpdatedAt = now },
            new User { ShopId = otherShop.Id, FullName = "Other Owner", Email = OtherEmail, PasswordHash = hasher.Hash(OtherPassword), CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public Task<HttpClient> UserClientAsync() => CreateClientAsAsync(UserEmail, UserPassword);
    public Task<HttpClient> OtherShopClientAsync() => CreateClientAsAsync(OtherEmail, OtherPassword);
}
