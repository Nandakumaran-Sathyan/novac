using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SmartPOS.Application.Reports;

namespace SmartPOS.Application.AI.Tools;

/// <summary>What happened when one tool ran. <c>Json</c> is exactly what the model is given.</summary>
public sealed record StoreToolResult(string Name, bool Success, string Json, TimeSpan Duration);

/// <summary>
/// The tool catalogue offered to the model, and the only way a model's tool call reaches the database.
/// For every call it checks the tool name, rejects unknown arguments (such as a <c>shopId</c> the model made
/// up), rejects invalid values, caps numbers to their allowed range, runs the tool, and wraps the result in
/// an envelope saying what was actually used. Any failure becomes an explicit error the model must report,
/// never a gap it could fill with invented figures. Read-only: no tool changes anything.
/// </summary>
public sealed class StoreToolRegistry(IStoreAITools tools, ILogger<StoreToolRegistry> logger)
{
    public const string RetrievalFailed = "The data could not be retrieved. Tell the user it is unavailable right now; do not guess.";

    // Relaxed escaping: this JSON goes only to the model, never into HTML, and \u0027-style escapes (or escaped
    // non-English product names) make it harder for the model to read.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] PeriodNames = [.. StorePeriods.ByName.Keys];

    private sealed record Param(string Name, string Description, int? Min = null, int? Max = null, int? DefaultInt = null,
        string[]? Values = null, string? DefaultValue = null)
    {
        public bool IsInteger => Values is null;
    }

    private sealed record Spec(string Name, string Description, Param[] Params,
        Func<IStoreAITools, IReadOnlyDictionary<string, object>, CancellationToken, Task<object>> Run);

    private static Param Period(string defaultValue) => new("period",
        "Time period: today, yesterday, last7days, thisMonth (1st of this month to today), lastMonth (the whole previous month) or last30days.",
        Values: PeriodNames, DefaultValue: defaultValue);

    private static StorePeriod P(IReadOnlyDictionary<string, object> a) => StorePeriods.ByName[(string)a["period"]];

    private static readonly Spec[] Specs =
    [
        new("GetTodaySales", "Total sales, number of bills and average bill for today (the shop's local day).", [],
            async (t, _, ct) => await t.GetSalesSummaryAsync(StorePeriod.Today, ct)),
        new("GetYesterdaySales", "Total sales, number of bills and average bill for yesterday.", [],
            async (t, _, ct) => await t.GetSalesSummaryAsync(StorePeriod.Yesterday, ct)),
        new("GetTopSellingProducts", "Best-selling products in a period, ranked by units sold (default) or by net sales revenue.",
            [Period("last30days"), new("limit", "How many products (1-20).", 1, 20, 5),
             new("rankBy", "units or revenue.", Values: ["units", "revenue"], DefaultValue: "units")],
            async (t, a, ct) => await t.GetTopProductsAsync(P(a), (int)a["limit"],
                (string)a["rankBy"] == "revenue" ? TopProductSort.Revenue : TopProductSort.Units, ct)),
        new("GetMostProfitableProducts", "Products that earned the most gross profit (net sales minus cost) in a period.",
            [Period("last30days"), new("limit", "How many products (1-20).", 1, 20, 5)],
            async (t, a, ct) => await t.GetTopProductsAsync(P(a), (int)a["limit"], TopProductSort.Profit, ct)),
        new("GetLowStockProducts", "Products at or below their low-stock level, including out of stock, most urgent first.",
            [new("limit", "How many products (1-50).", 1, 50, 20)],
            async (t, a, ct) => await t.GetLowStockProductsAsync((int)a["limit"], ct)),
        new("GetExpiringProducts", "Products that have expired or will expire within the given number of days, soonest first.",
            [new("withinDays", "Look this many days ahead (1-365).", 1, 365, 30), new("limit", "How many products (1-50).", 1, 50, 20)],
            async (t, a, ct) => await t.GetExpiringProductsAsync((int)a["withinDays"], (int)a["limit"], ct)),
        new("GetInventoryValue", "Current value of the stock on hand, at cost price and at selling price.", [],
            async (t, _, ct) => await t.GetInventoryValueAsync(ct)),
        new("GetUnsoldProducts", "Active products that have not sold at all in the last N days (dead stock), most stock value first.",
            [new("days", "Look back this many days (7-365).", 7, 365, 30), new("limit", "How many products (1-50).", 1, 50, 20)],
            async (t, a, ct) => await t.GetUnsoldProductsAsync((int)a["days"], (int)a["limit"], ct))
    ];

    /// <summary>Tool definitions (name, description, JSON Schema) to send to the model.</summary>
    public static IReadOnlyList<ToolDefinition> Definitions { get; } = Specs.Select(ToDefinition).ToList();

    public static IReadOnlyCollection<string> Names { get; } = Specs.Select(s => s.Name).ToList();

    public async Task<StoreToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var spec = Specs.FirstOrDefault(s => s.Name == call.Name);
        if (spec is null)
        {
            logger.LogWarning("AI requested unknown tool {Tool}", call.Name);
            return Fail(call.Name, $"There is no tool named '{call.Name}'. Available tools: {string.Join(", ", Names)}.", stopwatch);
        }

        var (arguments, adjustments, error) = Validate(spec, call.Arguments);
        if (error is not null)
        {
            logger.LogWarning("AI tool {Tool} rejected: {Error}", spec.Name, error);
            return Fail(spec.Name, error, stopwatch);
        }

        object data;
        try
        {
            data = await spec.Run(tools, arguments, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI tool {Tool} failed", spec.Name);
            return Fail(spec.Name, RetrievalFailed, stopwatch);
        }

        var envelope = new JsonObject
        {
            ["tool"] = spec.Name,
            ["arguments"] = JsonSerializer.SerializeToNode(arguments, Json),
            ["data"] = JsonSerializer.SerializeToNode(data, data.GetType(), Json)
        };
        if (adjustments.Count > 0)
            envelope["adjustments"] = new JsonArray([.. adjustments.Select(a => JsonValue.Create(a))]);

        stopwatch.Stop();
        logger.LogInformation("AI tool {Tool} ran in {DurationMs} ms (arguments: {Arguments})",
            spec.Name, stopwatch.ElapsedMilliseconds, string.Join(", ", arguments.Select(a => $"{a.Key}={a.Value}")));
        return new StoreToolResult(spec.Name, true, envelope.ToJsonString(Json), stopwatch.Elapsed);
    }

    /// <summary>
    /// Checks the model's arguments against the tool's specification. Unknown names and invalid values are
    /// errors; numbers outside their range are capped and reported, which spares a slow round trip to the model.
    /// </summary>
    private static (Dictionary<string, object> Arguments, List<string> Adjustments, string? Error) Validate(Spec spec, JsonElement raw)
    {
        var result = new Dictionary<string, object>();
        var adjustments = new List<string>();
        var allowed = spec.Params.Select(p => p.Name).ToList();

        if (raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in raw.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                    return (result, adjustments, allowed.Count == 0
                        ? $"{spec.Name} takes no arguments, but '{property.Name}' was given."
                        : $"Unknown argument '{property.Name}' for {spec.Name}. Allowed: {string.Join(", ", allowed)}.");
            }
        }

        foreach (var param in spec.Params)
        {
            JsonElement value = default;
            var present = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty(param.Name, out value) && value.ValueKind != JsonValueKind.Null;

            if (param.IsInteger)
            {
                if (!present)
                {
                    result[param.Name] = param.DefaultInt!.Value;
                    continue;
                }
                long number;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
                    number = n;
                else if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                    number = s;
                else
                    return (result, adjustments, $"Argument '{param.Name}' must be a whole number between {param.Min} and {param.Max}.");

                var capped = Math.Clamp(number, param.Min!.Value, param.Max!.Value);
                if (capped != number)
                    adjustments.Add($"{param.Name} {number} is outside {param.Min}-{param.Max}, so {capped} was used.");
                result[param.Name] = (int)capped;
            }
            else
            {
                if (!present)
                {
                    result[param.Name] = param.DefaultValue!;
                    continue;
                }
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                var match = param.Values!.FirstOrDefault(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return (result, adjustments, $"Argument '{param.Name}' must be one of: {string.Join(", ", param.Values!)}.");
                result[param.Name] = match;
            }
        }
        return (result, adjustments, null);
    }

    private StoreToolResult Fail(string name, string error, Stopwatch stopwatch) =>
        new(name, false, new JsonObject { ["tool"] = name, ["error"] = error }.ToJsonString(Json), stopwatch.Elapsed);

    private static ToolDefinition ToDefinition(Spec spec)
    {
        var properties = new JsonObject();
        foreach (var p in spec.Params)
        {
            properties[p.Name] = p.IsInteger
                ? new JsonObject { ["type"] = "integer", ["minimum"] = p.Min, ["maximum"] = p.Max, ["description"] = $"{p.Description} Default {p.DefaultInt}." }
                : new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. p.Values!.Select(v => JsonValue.Create(v))]), ["description"] = $"{p.Description} Default {p.DefaultValue}." };
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray() };
        return new ToolDefinition(spec.Name, spec.Description, JsonDocument.Parse(schema.ToJsonString()).RootElement.Clone());
    }
}
