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
using SmartPOS.Application.Auth;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;
using SmartPOS.Infrastructure.Persistence;

namespace SmartPOS.IntegrationTests;

/// <summary>
/// Runs the real API pipeline (auth, validation, controllers, services, EF Core) in memory.
/// SQLite in-memory replaces MySQL so the tests need no database server; MySQL-specific behaviour
/// (e.g. generated SQL, locking) is covered by the manual smoke test described in docs/TESTING.md.
/// </summary>
public sealed class SmartPosApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only credentials for a throwaway in-memory database.
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "AdminPass123";
    public const string CashierEmail = "cashier@test.local";
    public const string CashierPassword = "CashierPass123";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public int CategoryId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
        builder.UseSetting("Pos:TaxRatePercent", "5");
        builder.UseSetting("Pos:MaxCashierDiscountPercent", "10");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SmartPosDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SmartPosDbContext>>();
            services.AddDbContext<SmartPosDbContext>(options => options.UseSqlite(_connection));
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
        db.Users.AddRange(
            new User { FullName = "Test Admin", Email = AdminEmail, PasswordHash = hasher.Hash(AdminPassword), Role = UserRole.Admin, CreatedAt = now, UpdatedAt = now },
            new User { FullName = "Test Cashier", Email = CashierEmail, PasswordHash = hasher.Hash(CashierPassword), Role = UserRole.Cashier, CreatedAt = now, UpdatedAt = now });
        var category = new Category { Name = "Test Category", CreatedAt = now };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        CategoryId = category.Id;
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

    public Task<HttpClient> AdminClientAsync() => CreateClientAsAsync(AdminEmail, AdminPassword);
    public Task<HttpClient> CashierClientAsync() => CreateClientAsAsync(CashierEmail, CashierPassword);
}
