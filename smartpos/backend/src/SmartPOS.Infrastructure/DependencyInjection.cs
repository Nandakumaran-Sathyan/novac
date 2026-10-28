using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
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
        services.AddScoped<ISampleDataSeeder, SampleDataSeeder>();

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
        services.AddScoped<DemoHistoryCommand>();
        services.AddScoped<ResetPasswordCommand>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        return services;
    }
}
