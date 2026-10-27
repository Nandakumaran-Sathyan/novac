using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartPOS.Infrastructure.Persistence;

namespace SmartPOS.API.Infrastructure;

public sealed class DatabaseHealthCheck(SmartPosDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (Exception)
        {
            // Deliberately no exception text: it can contain host names or user names.
            return HealthCheckResult.Unhealthy("Database unreachable.");
        }
    }
}
