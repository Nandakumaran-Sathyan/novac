using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartPOS.Application.AI;
using SmartPOS.Application.AI.Tools;
using SmartPOS.Application.Reports;

namespace SmartPOS.UnitTests;

public class StoreToolRegistryTests
{
    /// <summary>Records what the registry asked for; returns tiny canned results.</summary>
    private sealed class FakeTools : IStoreAITools
    {
        public List<string> Calls { get; } = [];
        public Exception? Throw { get; set; }

        private T Record<T>(string call, T result)
        {
            Calls.Add(call);
            if (Throw is not null) throw Throw;
            return result;
        }

        public Task<SalesSummaryResult> GetSalesSummaryAsync(StorePeriod period, CancellationToken ct) =>
            Task.FromResult(Record($"sales:{period}", new SalesSummaryResult("x", default, default, "INR", 100m, 2, 50m, 5m, "n")));
        public Task<TopProductsResult> GetTopProductsAsync(StorePeriod period, int limit, TopProductSort rankBy, CancellationToken ct) =>
            Task.FromResult(Record($"top:{period}:{limit}:{rankBy}", new TopProductsResult("x", default, default, "INR", "units", [], "n")));
        public Task<LowStockResult> GetLowStockProductsAsync(int limit, CancellationToken ct) =>
            Task.FromResult(Record($"low:{limit}", new LowStockResult(0, [], "n")));
        public Task<ExpiringProductsResult> GetExpiringProductsAsync(int withinDays, int limit, CancellationToken ct) =>
            Task.FromResult(Record($"expiring:{withinDays}:{limit}", new ExpiringProductsResult(default, withinDays, 0, 0, [], "n")));
        public Task<InventoryValueResult> GetInventoryValueAsync(CancellationToken ct) =>
            Task.FromResult(Record("value", new InventoryValueResult("INR", 1m, 2m, 1m, 1, 1, "n")));
        public Task<UnsoldProductsResult> GetUnsoldProductsAsync(int days, int limit, CancellationToken ct) =>
            Task.FromResult(Record($"unsold:{days}:{limit}", new UnsoldProductsResult(days, default, "INR", 0, [], "n")));
    }

    private static (StoreToolRegistry Registry, FakeTools Tools) Create()
    {
        var tools = new FakeTools();
        return (new StoreToolRegistry(tools, NullLogger<StoreToolRegistry>.Instance), tools);
    }

    private static ToolCall Call(string name, string json = "{}") => new(name, JsonDocument.Parse(json).RootElement.Clone());

    private static JsonElement Parse(StoreToolResult r) => JsonDocument.Parse(r.Json).RootElement;

    [Fact]
    public void Offers_exactly_the_eight_approved_read_only_tools()
    {
        Assert.Equal(
            ["GetTodaySales", "GetYesterdaySales", "GetTopSellingProducts", "GetMostProfitableProducts",
             "GetLowStockProducts", "GetExpiringProducts", "GetInventoryValue", "GetUnsoldProducts"],
            StoreToolRegistry.Definitions.Select(d => d.Name));
    }

    [Fact]
    public void No_tool_schema_lets_the_model_choose_a_shop_tenant_user_or_date()
    {
        foreach (var definition in StoreToolRegistry.Definitions)
        {
            var schema = definition.ParametersSchema.GetRawText().ToLowerInvariant();
            foreach (var forbidden in new[] { "shop", "tenant", "user", "\"from\"", "\"to\"", "date\"" })
                Assert.DoesNotContain(forbidden, schema);
            Assert.Equal("object", definition.ParametersSchema.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task Defaults_are_applied_and_reported_in_the_envelope()
    {
        var (registry, tools) = Create();
        var result = await registry.ExecuteAsync(Call("GetTopSellingProducts"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("top:Last30Days:5:Units", Assert.Single(tools.Calls));
        var json = Parse(result);
        Assert.Equal("GetTopSellingProducts", json.GetProperty("tool").GetString());
        Assert.Equal("last30days", json.GetProperty("arguments").GetProperty("period").GetString());
        Assert.Equal(5, json.GetProperty("arguments").GetProperty("limit").GetInt32());
        Assert.True(json.TryGetProperty("data", out _));
        Assert.False(json.TryGetProperty("adjustments", out _));
    }

    [Fact]
    public async Task Arguments_are_read_case_insensitively_and_numeric_strings_are_accepted()
    {
        var (registry, tools) = Create();
        await registry.ExecuteAsync(Call("GetTopSellingProducts", """{"period":"THISMONTH","limit":"3","rankBy":"Revenue"}"""), CancellationToken.None);
        Assert.Equal("top:ThisMonth:3:Revenue", tools.Calls[0]);
    }

    [Fact]
    public async Task Out_of_range_numbers_are_capped_and_the_cap_is_reported()
    {
        var (registry, tools) = Create();
        var result = await registry.ExecuteAsync(Call("GetUnsoldProducts", """{"days":1000,"limit":0}"""), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("unsold:365:1", tools.Calls[0]);
        var adjustments = Parse(result).GetProperty("adjustments").EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains(adjustments, a => a!.Contains("days 1000"));
        Assert.Contains(adjustments, a => a!.Contains("limit 0"));
    }

    [Theory]
    [InlineData("GetTodaySales", """{"shopId":2}""", "takes no arguments")]
    [InlineData("GetTopSellingProducts", """{"period":"thisMonth","shopId":2}""", "Unknown argument 'shopId'")]
    [InlineData("GetTopSellingProducts", """{"tenantId":"x"}""", "Unknown argument 'tenantId'")]
    [InlineData("GetTopSellingProducts", """{"period":"2019"}""", "must be one of")]
    [InlineData("GetTopSellingProducts", """{"rankBy":"price"}""", "must be one of")]
    [InlineData("GetLowStockProducts", """{"limit":"lots"}""", "whole number")]
    [InlineData("GetLowStockProducts", """{"limit":2.5}""", "whole number")]
    [InlineData("DropTable", "{}", "no tool named")]
    public async Task Bad_calls_are_refused_without_touching_the_database(string tool, string args, string expected)
    {
        var (registry, tools) = Create();
        var result = await registry.ExecuteAsync(Call(tool, args), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(expected, Parse(result).GetProperty("error").GetString());
        Assert.Empty(tools.Calls);
    }

    [Fact]
    public async Task A_failing_tool_reports_that_the_data_is_unavailable_and_never_leaks_the_exception()
    {
        var (registry, tools) = Create();
        tools.Throw = new InvalidOperationException("connection string Password=secret");

        var result = await registry.ExecuteAsync(Call("GetInventoryValue"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StoreToolRegistry.RetrievalFailed, Parse(result).GetProperty("error").GetString());
        Assert.DoesNotContain("secret", result.Json);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var (registry, tools) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        tools.Throw = new OperationCanceledException(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.ExecuteAsync(Call("GetTodaySales"), cts.Token));
    }

    [Fact]
    public async Task Each_tool_routes_to_the_right_query()
    {
        var (registry, tools) = Create();
        foreach (var name in StoreToolRegistry.Names)
            Assert.True((await registry.ExecuteAsync(Call(name), CancellationToken.None)).Success, name);
        Assert.Equal(
            ["sales:Today", "sales:Yesterday", "top:Last30Days:5:Units", "top:Last30Days:5:Profit", "low:20", "expiring:30:20", "value", "unsold:30:20"],
            tools.Calls);
    }

    [Theory]
    [InlineData(StorePeriod.Today, "2026-10-30", "2026-10-30")]
    [InlineData(StorePeriod.Yesterday, "2026-10-29", "2026-10-29")]
    [InlineData(StorePeriod.Last7Days, "2026-10-24", "2026-10-30")]
    [InlineData(StorePeriod.ThisMonth, "2026-10-01", "2026-10-30")]
    [InlineData(StorePeriod.LastMonth, "2026-09-01", "2026-09-30")]
    [InlineData(StorePeriod.Last30Days, "2026-10-01", "2026-10-30")]
    public void Periods_resolve_to_store_calendar_dates(StorePeriod period, string from, string to)
    {
        var (f, t) = StorePeriods.Resolve(period, new DateOnly(2026, 10, 30));
        Assert.Equal(DateOnly.Parse(from), f);
        Assert.Equal(DateOnly.Parse(to), t);
    }

    [Fact]
    public void Last_month_handles_january_and_leap_years()
    {
        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)), StorePeriods.Resolve(StorePeriod.LastMonth, new DateOnly(2026, 1, 15)));
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), StorePeriods.Resolve(StorePeriod.LastMonth, new DateOnly(2028, 3, 3)));
    }

    [Fact]
    public async Task Output_is_plain_readable_json_for_the_model()
    {
        var (registry, _) = Create();
        var result = await registry.ExecuteAsync(Call("GetInventoryValue"), CancellationToken.None);
        Assert.DoesNotContain("\\u00", result.Json);   // no \u0027-style escapes
    }
}
