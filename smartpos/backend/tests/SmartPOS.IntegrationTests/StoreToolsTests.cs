using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.Application.AI;
using SmartPOS.Application.AI.Tools;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Products;
using SmartPOS.Application.Reports;

namespace SmartPOS.IntegrationTests;

/// <summary>
/// The real tools against the real (shop-filtered) database, run as a signed-in shop exactly as a request
/// would run them. Each test creates its own uniquely named data and checks figures against it.
/// </summary>
public class StoreToolsTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    /// <summary>Runs <paramref name="work"/> as the given user and shop: the same claims the JWT would carry.</summary>
    private async Task<T> AsShop<T>(HttpClient client, Func<IServiceProvider, Task<T>> work)
    {
        var me = (await client.GetFromJsonAsync<UserDto>("/api/auth/me", SmartPosApiFactory.Json))!;
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", me.Id.ToString()), new Claim("shop", me.ShopId.ToString())], "test"))
        };
        try { return await work(scope.ServiceProvider); }
        finally { accessor.HttpContext = null; }
    }

    private static async Task<ProductDto> Product(HttpClient client, string sku, decimal price, decimal cost, int stock,
        int lowStockThreshold = 1, string? expiry = null)
    {
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            sku, name = $"Tool {sku}", category = "Tools", price, costPrice = cost, initialStock = stock, lowStockThreshold, expiryDate = expiry
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(SmartPosApiFactory.Json))!;
    }

    private static async Task Sell(HttpClient client, params (int ProductId, int Quantity)[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/sales", new { items = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Today_sales_match_the_bills_just_made()
    {
        var user = await factory.UserClientAsync();
        var before = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetSalesSummaryAsync(StorePeriod.Today, default));
        var product = await Product(user, "T-SALE", 100m, 60m, 10);
        await Sell(user, (product.Id, 2));   // 200 + 5% tax = 210
        await Sell(user, (product.Id, 1));   // 100 + 5% = 105

        var after = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetSalesSummaryAsync(StorePeriod.Today, default));

        Assert.Equal(before.TotalSales + 315m, after.TotalSales);
        Assert.Equal(before.Bills + 2, after.Bills);
        Assert.Equal(before.TaxCollected + 15m, after.TaxCollected);
        Assert.Equal("today", after.Period);
        Assert.Equal(after.From, after.To);
    }

    [Fact]
    public async Task Top_products_rank_by_units_by_revenue_and_by_profit_differently()
    {
        var user = await factory.UserClientAsync();
        var many = await Product(user, "T-MANY", 10m, 9m, 100);     // 30 units, revenue 300, profit 30
        var dear = await Product(user, "T-DEAR", 500m, 100m, 10);   // 1 unit, revenue 500, profit 400
        var mid = await Product(user, "T-MID", 200m, 50m, 10);      // 2 units, revenue 400, profit 300
        await Sell(user, (many.Id, 30), (dear.Id, 1), (mid.Id, 2));

        var tools = (Func<IServiceProvider, IStoreAITools>)(sp => sp.GetRequiredService<IStoreAITools>());
        string[] Mine(TopProductsResult r) => [.. r.Products.Select(p => p.Sku).Where(s => s is "T-MANY" or "T-DEAR" or "T-MID")];

        var byUnits = await AsShop(user, sp => tools(sp).GetTopProductsAsync(StorePeriod.Today, 20, TopProductSort.Units, default));
        var byRevenue = await AsShop(user, sp => tools(sp).GetTopProductsAsync(StorePeriod.Today, 20, TopProductSort.Revenue, default));
        var byProfit = await AsShop(user, sp => tools(sp).GetTopProductsAsync(StorePeriod.Today, 20, TopProductSort.Profit, default));

        Assert.Equal(["T-MANY", "T-MID", "T-DEAR"], Mine(byUnits));
        Assert.Equal(["T-DEAR", "T-MID", "T-MANY"], Mine(byRevenue));
        Assert.Equal(["T-DEAR", "T-MID", "T-MANY"], Mine(byProfit));
        var dearRow = byProfit.Products.Single(p => p.Sku == "T-DEAR");
        Assert.Equal(500m, dearRow.NetSales);
        Assert.Equal(400m, dearRow.GrossProfit);
    }

    [Fact]
    public async Task Low_stock_expiring_and_unsold_products_are_found()
    {
        var user = await factory.UserClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Product(user, "T-LOW", 5m, 2m, 3, lowStockThreshold: 5);
        await Product(user, "T-EXPIRED", 5m, 2m, 4, expiry: today.AddDays(-3).ToString("yyyy-MM-dd"));
        await Product(user, "T-SOON", 5m, 2m, 4, expiry: today.AddDays(5).ToString("yyyy-MM-dd"));
        await Product(user, "T-LATER", 5m, 2m, 4, expiry: today.AddDays(200).ToString("yyyy-MM-dd"));
        await Product(user, "T-DEAD", 7m, 3m, 9);
        var sold = await Product(user, "T-SOLD", 5m, 2m, 9);
        await Sell(user, (sold.Id, 1));

        var low = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetLowStockProductsAsync(50, default));
        Assert.Contains(low.Products, p => p.Sku == "T-LOW" && p.InStock == 3 && p.LowStockLevel == 5 && p.Status == "low");

        var expiring = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetExpiringProductsAsync(30, 50, default));
        var skus = expiring.Products.Select(p => p.Sku).ToList();
        Assert.Contains("T-EXPIRED", skus);
        Assert.Contains("T-SOON", skus);
        Assert.DoesNotContain("T-LATER", skus);
        Assert.True(expiring.Products.Single(p => p.Sku == "T-EXPIRED").DaysLeft < 0);
        Assert.True(skus.IndexOf("T-EXPIRED") < skus.IndexOf("T-SOON")); // soonest first
        Assert.True(expiring.AlreadyExpired >= 1 && expiring.ExpiringSoon >= 1);

        var unsold = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetUnsoldProductsAsync(30, 50, default));
        var dead = unsold.Products.Single(p => p.Sku == "T-DEAD");
        Assert.Null(dead.LastSoldOn);
        Assert.Equal(27m, dead.StockValueAtCost); // 9 x 3
        Assert.DoesNotContain(unsold.Products, p => p.Sku == "T-SOLD");
    }

    [Fact]
    public async Task Inventory_value_rises_by_exactly_the_new_stock()
    {
        var user = await factory.UserClientAsync();
        var before = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetInventoryValueAsync(default));
        await Product(user, "T-VALUE", 8m, 5m, 10);

        var after = await AsShop(user, sp => sp.GetRequiredService<IStoreAITools>().GetInventoryValueAsync(default));

        Assert.Equal(before.ValueAtCost + 50m, after.ValueAtCost);
        Assert.Equal(before.ValueAtSellingPrice + 80m, after.ValueAtSellingPrice);
        Assert.Equal(before.UnitsInStock + 10, after.UnitsInStock);
        Assert.Equal(before.ProductsInStock + 1, after.ProductsInStock);
    }

    [Fact]
    public async Task Another_shop_sees_none_of_this_shops_data_through_any_tool()
    {
        var user = await factory.UserClientAsync();
        var other = await factory.OtherShopClientAsync();
        var mine = await Product(user, "T-PRIVATE", 999m, 1m, 2, lowStockThreshold: 5, expiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd"));
        await Sell(user, (mine.Id, 1));

        // Every tool, through the registry exactly as the model will call it, as the OTHER shop.
        var outputs = await AsShop(other, async sp =>
        {
            var registry = sp.GetRequiredService<StoreToolRegistry>();
            var all = new List<string>();
            foreach (var name in StoreToolRegistry.Names)
            {
                var takesLimit = StoreToolRegistry.Definitions.Single(d => d.Name == name)
                    .ParametersSchema.GetProperty("properties").TryGetProperty("limit", out _);
                var args = JsonDocument.Parse(takesLimit ? "{\"limit\":50}" : "{}").RootElement.Clone();
                var r = await registry.ExecuteAsync(new ToolCall(name, args), default);
                Assert.True(r.Success, $"{name}: {r.Json}");
                all.Add(r.Json);
            }
            return all;
        });

        Assert.All(outputs, json => Assert.DoesNotContain("T-PRIVATE", json));
        Assert.DoesNotContain(outputs, json => json.Contains("999"));   // the price never shows up in totals either

        // ...while the owning shop does see it.
        var own = await AsShop(user, sp => sp.GetRequiredService<StoreToolRegistry>()
            .ExecuteAsync(new ToolCall("GetLowStockProducts", JsonDocument.Parse("{\"limit\":50}").RootElement.Clone()), default));
        Assert.Contains("T-PRIVATE", own.Json);
    }

    [Fact]
    public async Task Without_a_signed_in_shop_the_tools_see_nothing()
    {
        // No HttpContext at all: the shop filter fails closed instead of exposing every shop.
        using var scope = factory.Services.CreateScope();
        var value = await scope.ServiceProvider.GetRequiredService<IStoreAITools>().GetInventoryValueAsync(default);
        Assert.Equal(0, value.ProductsInStock);
        Assert.Equal(0m, value.ValueAtCost);
    }
}
