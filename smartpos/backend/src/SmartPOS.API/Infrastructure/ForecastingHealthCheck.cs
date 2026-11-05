using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartPOS.Application.Forecasting;

namespace SmartPOS.API.Infrastructure;

/// <summary>Reports whether the forecasting service answers. Degraded on failure: selling works without it.</summary>
public sealed class ForecastingHealthCheck(IForecastingClient forecasting) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await forecasting.GetJsonAsync("health", cancellationToken);
            return HealthCheckResult.Healthy("Forecasting service reachable.");
        }
        catch (ForecastingUnavailableException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, ex.Message);
        }
    }
}
