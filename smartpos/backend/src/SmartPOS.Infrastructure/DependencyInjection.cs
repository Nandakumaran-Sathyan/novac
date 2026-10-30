using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Application.AI;
using SmartPOS.Infrastructure.AI;
using SmartPOS.Infrastructure.Email;
using SmartPOS.Infrastructure.Persistence;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "SmartPos";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SmartPosDbContext>(options =>
        {
            // Read lazily (when the first DbContext is created) so a missing value produces a clear
            // error instead of a failed connection attempt, and so tests can swap the provider.
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is not configured. Set it with `dotnet user-secrets` " +
                    $"or the ConnectionStrings__{ConnectionStringName} environment variable (see docs/DATABASE.md).");
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SmartPosDbContext>());

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<SampleDataSeeder>();
        services.AddScoped<ISampleDataSeeder>(sp => sp.GetRequiredService<SampleDataSeeder>());
        services.AddScoped<ResetDemoDataCommand>();

        // Email: real SMTP when Email:Smtp:Host is set; otherwise (development) log the message; otherwise do nothing.
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        var smtpConfigured = !string.IsNullOrWhiteSpace(configuration[$"{SmtpOptions.SectionName}:Host"]);
        if (smtpConfigured)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else if (configuration.GetValue<bool>("Email:LogMessages"))
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        else
            services.AddSingleton<IEmailSender, NullEmailSender>();
        services.AddSingleton<IEmailAddressChecker, DnsOverHttpsEmailAddressChecker>();

        // AI model (Ollama). Validated at startup, but Ollama itself need not be running for the API to start.
        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https",
                "Ollama:BaseUrl must be an absolute http(s) URL.")
            .Validate(o => o.AllowRemoteHost || IsLoopback(o.BaseUrl),
                "Ollama:BaseUrl must point at this machine (localhost). Shop data must not leave it; set Ollama:AllowRemoteHost only for a trusted private network.")
            .Validate(o => !IsCloudModel(o.Model),
                "Ollama:Model is a cloud model, which would send shop data to a third party. Use a local model.")
            .ValidateOnStart();
        services.AddHttpClient<IChatModelClient, OllamaChatClient>((sp, http) =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });
        services.AddScoped<DemoHistoryCommand>();
        services.AddScoped<ResetPasswordCommand>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        return services;
    }

    /// <summary>True for localhost, 127.x.x.x and ::1.</summary>
    internal static bool IsLoopback(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
        (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>Ollama's cloud-proxied models are tagged ":cloud" or "-cloud" (e.g. glm-5.1:cloud).</summary>
    internal static bool IsCloudModel(string model) =>
        model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);
}
