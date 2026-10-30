using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartPOS.Application.AI;

namespace SmartPOS.API.Infrastructure;

/// <summary>
/// Reports whether the AI model server and model are ready. Registered as Degraded on failure. /health is public,
/// so the message never names the model or server version (signed-in users can see those at /api/ai/status).
/// </summary>
public sealed class AIModelHealthCheck(IChatModelClient model) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await model.GetStatusAsync(cancellationToken);
        return status.Ready
            ? HealthCheckResult.Healthy("AI model ready.")
            : new HealthCheckResult(context.Registration.FailureStatus, status.Problem);
    }
}
