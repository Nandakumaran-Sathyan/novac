using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace SmartPOS.IntegrationTests;

/// <summary>The /api/forecasting endpoints forward a signed-in owner's requests to the forecasting service, always for the
/// owner's own shop (from the token), never a shop named in the request.</summary>
public sealed class ForecastingTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    private FakeForecastingHandler Fake => factory.Forecasting;

    private void Reset(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        Fake.Requests.Clear();
        Fake.Respond = respond;
    }

    [Fact]
    public async Task Forecasting_needs_a_signed_in_user()
    {
        Reset();
        var response = await factory.CreateClient().GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task Requests_are_forwarded_for_the_callers_shop_with_the_service_key_and_the_json_comes_back_unchanged()
    {
        const string payload = """{"run":{"model_version":"shops-lgbm-test"},"shop_id":1,"items":[]}""";
        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.OK, payload));
        var user = await factory.UserClientAsync();

        var response = await user.GetAsync("/api/forecasting/forecast?horizon=5&category=Dairy%20%26%20Eggs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsStringAsync());
        var sent = Assert.Single(Fake.Requests);
        Assert.Equal($"/shops/{factory.ShopId}/forecast?horizon=5&category=Dairy%20%26%20Eggs", sent.PathAndQuery);
        Assert.Equal(SmartPosApiFactory.ForecastingKey, sent.Key);
    }

    [Theory]
    [InlineData("/api/forecasting/status", "status")]
    [InlineData("/api/forecasting/forecast", "forecast?horizon=7")]
    [InlineData("/api/forecasting/categories?horizon=3", "categories/forecast?horizon=3")]
    [InlineData("/api/forecasting/stockout-alerts?risk=high&risk=LOW", "stockout-alerts?risk=HIGH&risk=LOW")]
    [InlineData("/api/forecasting/stockout-alerts", "stockout-alerts?risk=HIGH&risk=MEDIUM")]
    [InlineData("/api/forecasting/replenishment?onlyNeeded=false", "replenishment-recommendations?only_needed=false")]
    [InlineData("/api/forecasting/products/12/forecast?horizon=2", "products/12/forecast?horizon=2")]
    [InlineData("/api/forecasting/metrics?evaluation=backtest", "model-metrics?evaluation=backtest")]
    public async Task Each_endpoint_maps_to_the_callers_shop_in_the_service(string url, string expected)
    {
        Reset();
        var response = await (await factory.UserClientAsync()).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/shops/{factory.ShopId}/{expected}", Assert.Single(Fake.Requests).PathAndQuery);
    }

    [Fact]
    public async Task Each_owner_only_ever_reaches_their_own_shop()
    {
        Assert.NotEqual(factory.ShopId, factory.OtherShopId);
        Reset();
        await (await factory.UserClientAsync()).GetAsync("/api/forecasting/stockout-alerts");
        await (await factory.OtherShopClientAsync()).GetAsync("/api/forecasting/stockout-alerts");
        await (await factory.OtherShopClientAsync()).PostAsync("/api/forecasting/generate", null);
        Assert.Equal(
            [$"/shops/{factory.ShopId}/stockout-alerts?risk=HIGH&risk=MEDIUM",
             $"/shops/{factory.OtherShopId}/stockout-alerts?risk=HIGH&risk=MEDIUM",
             $"/shops/{factory.OtherShopId}/forecast/generate"],
            Fake.Requests.Select(r => r.PathAndQuery).ToArray());
    }

    [Theory]
    [InlineData("/api/forecasting/forecast?shopId=99")]
    [InlineData("/api/forecasting/forecast?shop_id=99")]
    [InlineData("/api/forecasting/stores/1/forecast")]
    public async Task A_shop_in_the_request_is_ignored_or_unknown(string url)
    {
        Reset();
        var response = await (await factory.UserClientAsync()).GetAsync(url);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
        Assert.All(Fake.Requests, r => Assert.StartsWith($"/shops/{factory.ShopId}/", r.PathAndQuery));
    }

    [Theory]
    [InlineData("/api/forecasting/forecast?horizon=9")]
    [InlineData("/api/forecasting/forecast?horizon=0")]
    [InlineData("/api/forecasting/stockout-alerts?risk=SEVERE")]
    [InlineData("/api/forecasting/metrics?evaluation=everything")]
    [InlineData("/api/forecasting/products/0/forecast")]
    public async Task Bad_values_are_refused_before_anything_is_forwarded(string url)
    {
        Reset();
        var response = await (await factory.UserClientAsync()).GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Fake.Requests);
    }

    [Fact]
    public async Task A_category_name_is_limited_and_escaped()
    {
        Reset();
        var user = await factory.UserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync($"/api/forecasting/forecast?category={new string('x', 101)}")).StatusCode);
        Assert.Empty(Fake.Requests);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/forecasting/forecast?category=../../shops/2")).StatusCode);
        Assert.Equal($"/shops/{factory.ShopId}/forecast?horizon=7&category=..%2F..%2Fshops%2F2", Assert.Single(Fake.Requests).PathAndQuery);
    }

    [Fact]
    public async Task Service_errors_become_the_apis_own_problem_responses()
    {
        var user = await factory.UserClientAsync();

        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.NotFound, """{"detail":"No forecast has been generated for this shop yet."}"""));
        var missing = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("No forecast has been generated for this shop yet.", await missing.Content.ReadAsStringAsync());

        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"detail":[{"loc":["query","horizon"],"msg":"Input should be less than or equal to 7"}]}"""));
        var invalid = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("horizon: Input should be less than or equal to 7", await invalid.Content.ReadAsStringAsync());

        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.Conflict, """{"detail":"Forecasts need at least 7 days of recorded sales; this shop has 2."}"""));
        var tooNew = await user.PostAsync("/api/forecasting/generate", null);
        Assert.Equal(HttpStatusCode.Conflict, tooNew.StatusCode);
        Assert.Contains("at least 7 days of recorded sales", await tooNew.Content.ReadAsStringAsync());

        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.ServiceUnavailable, """{"detail":"Forecasting is not set up yet: no model has been trained."}"""));
        var notReady = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
        Assert.Contains("no model has been trained", await notReady.Content.ReadAsStringAsync());

        Reset(_ => throw new HttpRequestException("connection refused"));
        var offline = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
        Assert.Contains("The forecasting service is offline", await offline.Content.ReadAsStringAsync());

        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.Unauthorized, """{"detail":"Missing or invalid X-Service-Key."}"""));
        var badKey = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, badKey.StatusCode);
        Assert.DoesNotContain(SmartPosApiFactory.ForecastingKey, await badKey.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Generate_posts_for_the_callers_shop_and_is_rate_limited()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Forecasting:GeneratePerMinute", "1"));
        Reset(_ => FakeForecastingHandler.Json(HttpStatusCode.OK, """{"run_id":"r1","forecast_rows":112}"""));
        var user = await LoginAsync(limited);

        var first = await user.PostAsync("/api/forecasting/generate", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var sent = Assert.Single(Fake.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"/shops/{factory.ShopId}/forecast/generate", sent.PathAndQuery);
        Assert.Equal(7, JsonDocument.Parse(sent.Body!).RootElement.GetProperty("horizon").GetInt32());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await user.PostAsync("/api/forecasting/generate", null)).StatusCode);
    }

    [Fact]
    public async Task Without_a_service_key_forecasting_is_switched_off()
    {
        using var unconfigured = factory.WithWebHostBuilder(b => b.UseSetting("Forecasting:ServiceKey", ""));
        Reset();
        var user = await LoginAsync(unconfigured);
        var response = await user.GetAsync("/api/forecasting/forecast");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("not configured", await response.Content.ReadAsStringAsync());
        Assert.Empty(Fake.Requests);
    }

    private static async Task<HttpClient> LoginAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = SmartPosApiFactory.UserEmail, password = SmartPosApiFactory.UserPassword });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<SmartPOS.Application.Auth.LoginResponse>(SmartPosApiFactory.Json))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
