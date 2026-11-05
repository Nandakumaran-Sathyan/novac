using SmartPOS.Application.Common;

namespace SmartPOS.Application.Forecasting;

/// <summary>
/// The forecasting service (a separate Python service on this machine; see forecasting/README.md). The API forwards
/// signed-in owners' requests to it, always under /shops/{the caller's shop}/, and returns its JSON unchanged. The
/// service computes each shop's forecasts from that shop's own SmartPOS sales and stock (read-only).
/// </summary>
public interface IForecastingClient
{
    /// <summary>GET a path (with query string) on the forecasting service and return the JSON body.</summary>
    /// <exception cref="ForecastingUnavailableException">The service is down, too slow, or not configured.</exception>
    /// <exception cref="NotFoundException">No forecast yet for the shop, or a product not in it.</exception>
    /// <exception cref="BusinessRuleException">The service rejected the request's values.</exception>
    Task<string> GetJsonAsync(string pathAndQuery, CancellationToken cancellationToken);

    /// <summary>POST a JSON body and return the JSON response.</summary>
    Task<string> PostJsonAsync(string path, object body, CancellationToken cancellationToken);
}

/// <summary>503: forecasting is unavailable. The message is safe to show to users; the technical cause is logged.</summary>
public sealed class ForecastingUnavailableException(string message) : AppException(message);
