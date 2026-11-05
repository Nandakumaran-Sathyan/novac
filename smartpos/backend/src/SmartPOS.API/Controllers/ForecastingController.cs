using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartPOS.API.Infrastructure;
using SmartPOS.Application.Common;
using SmartPOS.Application.Forecasting;

namespace SmartPOS.API.Controllers;

/// <summary>
/// Demand, stockout and replenishment forecasts (beta) for the signed-in owner's own shop, learned from the shop's
/// recorded sales. The shop always comes from the caller's token, never from the request, so a shop can only see
/// its own forecasts. Values are checked here before anything is forwarded, and the forecasting service's JSON is
/// returned unchanged (see forecasting/README.md for the response shapes).
/// </summary>
[ApiController]
[Route("api/forecasting")]
[Produces("application/json")]
public sealed class ForecastingController(IForecastingClient forecasting, ICurrentUser currentUser) : ControllerBase
{
    private string Shop => $"shops/{currentUser.ShopId}";

    /// <summary>Whether forecasting is ready for this shop: the active model and the shop's latest forecast run.</summary>
    [HttpGet("status")]
    public Task<ContentResult> Status(CancellationToken ct) => Forward($"{Shop}/status", ct);

    /// <summary>Daily product forecasts for the shop, with stock, risk and reorder quantity.</summary>
    [HttpGet("forecast")]
    public Task<ContentResult> ShopForecast([FromQuery] int horizon = 7, [FromQuery] string? category = null, CancellationToken ct = default) =>
        Forward(Query($"{Shop}/forecast", ("horizon", Horizon(horizon)), ("category", Category(category))), ct);

    /// <summary>Category totals: always the sum of that category's product forecasts.</summary>
    [HttpGet("categories")]
    public Task<ContentResult> CategoryForecast([FromQuery] int horizon = 7, CancellationToken ct = default) =>
        Forward(Query($"{Shop}/categories/forecast", ("horizon", Horizon(horizon))), ct);

    /// <summary>HIGH / MEDIUM / LOW stockout risk (transparent rules, not probabilities) with the expected stockout date.</summary>
    [HttpGet("stockout-alerts")]
    public Task<ContentResult> StockoutAlerts([FromQuery] string[]? risk = null, CancellationToken ct = default)
    {
        var levels = (risk is { Length: > 0 } ? risk : ["HIGH", "MEDIUM"]).Select(r => r.Trim().ToUpperInvariant()).ToArray();
        if (levels.Any(r => r is not ("HIGH" or "MEDIUM" or "LOW")))
            throw new BusinessRuleException("risk must be HIGH, MEDIUM or LOW.");
        return Forward(Query($"{Shop}/stockout-alerts", [.. levels.Select(l => ("risk", (string?)l))]), ct);
    }

    [HttpGet("replenishment")]
    public Task<ContentResult> Replenishment([FromQuery] bool onlyNeeded = true, CancellationToken ct = default) =>
        Forward(Query($"{Shop}/replenishment-recommendations", ("only_needed", onlyNeeded ? "true" : "false")), ct);

    /// <summary>One product's forecast; 404 unless the product belongs to the caller's shop.</summary>
    [HttpGet("products/{productId:int}/forecast")]
    public Task<ContentResult> ProductForecast(int productId, [FromQuery] int horizon = 7, CancellationToken ct = default) =>
        productId > 0
            ? Forward(Query($"{Shop}/products/{productId}/forecast", ("horizon", Horizon(horizon))), ct)
            : throw new BusinessRuleException("Product id must be positive.");

    /// <summary>Accuracy of the active model against the two baselines: overall, and on this shop.</summary>
    [HttpGet("metrics")]
    public Task<ContentResult> Metrics([FromQuery] string evaluation = "test", CancellationToken ct = default) =>
        evaluation is "validation" or "test" or "backtest"
            ? Forward(Query($"{Shop}/model-metrics", ("evaluation", evaluation)), ct)
            : throw new BusinessRuleException("evaluation must be validation, test or backtest.");

    /// <summary>Refresh this shop's 7-day forecast from its latest sales (a few seconds). Limited to a few runs per minute.</summary>
    [HttpPost("generate")]
    [EnableRateLimiting(ApiServiceCollectionExtensions.ForecastGenerateRateLimitPolicy)]
    public async Task<ContentResult> Generate(CancellationToken ct) =>
        Json(await forecasting.PostJsonAsync($"{Shop}/forecast/generate", new { horizon = 7 }, ct));

    private async Task<ContentResult> Forward(string pathAndQuery, CancellationToken ct) =>
        Json(await forecasting.GetJsonAsync(pathAndQuery, ct));

    private static ContentResult Json(string body) => new() { Content = body, ContentType = "application/json", StatusCode = 200 };

    private static string Query(string path, params (string Key, string? Value)[] items)
    {
        var parts = items.Where(i => i.Value is not null).Select(i => $"{i.Key}={Uri.EscapeDataString(i.Value!)}").ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private static string? Category(string? name) =>
        name is null || name.Trim().Length is >= 1 and <= 100 ? name?.Trim() : throw new BusinessRuleException("Category must be 1-100 characters.");
    private static string Horizon(int days) => days is >= 1 and <= 7 ? days.ToString() : throw new BusinessRuleException("Horizon must be 1-7 days.");
}
