using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
using SmartPOS.Application.Forecasting;

namespace SmartPOS.Infrastructure.Forecasting;

/// <summary>Bound from the "Forecasting" configuration section. ServiceKey belongs in user-secrets or an environment variable.</summary>
public sealed class ForecastingOptions
{
    public const string SectionName = "Forecasting";

    /// <summary>Where the forecasting service listens. Only this API talks to it.</summary>
    [Required]
    public string BaseUrl { get; set; } = "http://127.0.0.1:8090";

    /// <summary>Shared secret sent as X-Service-Key (at least 32 characters). Empty = forecasting switched off.</summary>
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Off by default: the service must run on this machine unless deliberately allowed.</summary>
    public bool AllowRemoteHost { get; set; }

    [Range(2, 300)]
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Typed HttpClient for the forecasting service. It adds the service key, and turns the service's answers into the
/// API's own error types, so users see the same problem+json errors as everywhere else. Response bodies are
/// forwarded unchanged and never logged.
/// </summary>
public sealed class ForecastingClient(HttpClient http, IOptions<ForecastingOptions> options, ILogger<ForecastingClient> logger)
    : IForecastingClient
{
    public Task<string> GetJsonAsync(string pathAndQuery, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, pathAndQuery), cancellationToken);

    public Task<string> PostJsonAsync(string path, object body, CancellationToken cancellationToken) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }, cancellationToken);

    private async Task<string> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        var key = options.Value.ServiceKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new ForecastingUnavailableException("Forecasting is not configured on this server (Forecasting:ServiceKey).");

        using var request = build();
        request.Headers.Add("X-Service-Key", key);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Forecasting service timed out after {Timeout}s", options.Value.TimeoutSeconds);
            throw new ForecastingUnavailableException("The forecasting service took too long to answer. Try again in a moment.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Forecasting service is not reachable at {BaseUrl}", options.Value.BaseUrl);
            throw new ForecastingUnavailableException("The forecasting service is offline. Start it and try again.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return body;
            var detail = Detail(body);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new NotFoundException(detail ?? "Not found.");
                case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                    throw new BusinessRuleException(detail ?? "The forecasting request was not valid.");
                case HttpStatusCode.Conflict:
                    throw new ConflictException(detail ?? "A forecast run is already in progress.");
                case HttpStatusCode.Unauthorized:
                    logger.LogError("The forecasting service rejected the API's service key; check Forecasting:ServiceKey on both sides");
                    throw new ForecastingUnavailableException("Forecasting is misconfigured on this server.");
                case HttpStatusCode.ServiceUnavailable:
                    throw new ForecastingUnavailableException(detail ?? "Forecasting is not ready yet.");
                default:
                    logger.LogError("Forecasting service returned {Status}", (int)response.StatusCode);
                    throw new ForecastingUnavailableException("The forecasting service returned an error. Try again in a moment.");
            }
        }
    }

    /// <summary>FastAPI errors are {"detail": "..."} or {"detail": [{"msg": "...", "loc": [...]}]}.</summary>
    private static string? Detail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("detail", out var detail))
                return null;
            if (detail.ValueKind == JsonValueKind.String)
                return detail.GetString();
            if (detail.ValueKind == JsonValueKind.Array && detail.GetArrayLength() > 0 && detail[0].TryGetProperty("msg", out var msg))
            {
                var field = detail[0].TryGetProperty("loc", out var loc) && loc.GetArrayLength() > 0 ? loc[loc.GetArrayLength() - 1].ToString() : null;
                return field is null ? msg.GetString() : $"{field}: {msg.GetString()}";
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }
}
