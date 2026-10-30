using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.Application.Common;
using SmartPOS.Application.Customers;
using SmartPOS.Application.Products;
using SmartPOS.Infrastructure.Persistence;

namespace SmartPOS.IntegrationTests;

/// <summary>The reset-demo-data command changes exactly one shop's data and leaves every other shop alone.</summary>
public class ResetDemoDataTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    private static async Task<List<ProductDto>> Products(HttpClient c) =>
        (await c.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?pageSize=100", SmartPosApiFactory.Json))!.Items.ToList();

    private static async Task<List<CustomerDto>> Customers(HttpClient c) =>
        (await c.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers?pageSize=100", SmartPosApiFactory.Json))!.Items.ToList();

    [Fact]
    public async Task One_shop_becomes_a_pharmacy_and_the_other_shop_is_untouched()
    {
        var grocery = await factory.UserClientAsync();
        var pharmacy = await factory.OtherShopClientAsync();
        await grocery.PostAsJsonAsync("/api/products", new { sku = "KEEP-ME", name = "Keep me", category = "Test", price = 5, costPrice = 2, initialStock = 3 });
        await grocery.PostAsJsonAsync("/api/customers", new { name = "Grocery Regular", phone = "9555500001" });
        await pharmacy.PostAsJsonAsync("/api/products", new { sku = "OLD-ITEM", name = "Old item", category = "Old", price = 5, costPrice = 2, initialStock = 3 });
        var groceryBefore = (await Products(grocery)).Select(p => p.Sku).OrderBy(s => s).ToList();
        var groceryCustomersBefore = (await Customers(grocery)).Select(c => c.Phone).OrderBy(p => p).ToList();

        int createdProducts;
        // Discounts off for this run: SQLite compares decimals as text, so its copy of the DiscountPercent CHECK
        // wrongly rejects 5% and 15% (PostgreSQL does not; see docs/TESTING.md). Same in-memory database.
        using var noDiscounts = factory.WithWebHostBuilder(b => b.UseSetting("Pos:MaxDiscountPercent", "0"));
        using (var scope = noDiscounts.Services.CreateScope())
        {
            var (deleted, created) = await scope.ServiceProvider.GetRequiredService<ResetDemoDataCommand>()
                .RunAsync(SmartPosApiFactory.OtherEmail, "pharmacy", days: 7, default);
            Assert.True(deleted.Products >= 1);
            Assert.Equal("pharmacy", created.Profile);
            Assert.True(created.Products >= 10);
            Assert.True(created.Sales > 0);
            createdProducts = created.Products;
        }

        // The pharmacy shop now has only pharmacy products and pharmacy customers.
        var pharmacyProducts = await Products(pharmacy);
        Assert.Equal(createdProducts, pharmacyProducts.Count);
        Assert.All(pharmacyProducts, p => Assert.StartsWith("PHM-", p.Sku));
        Assert.DoesNotContain(pharmacyProducts, p => p.Sku == "OLD-ITEM");
        Assert.All(await Customers(pharmacy), c => Assert.StartsWith("90000002", c.Phone));

        // The grocery shop is exactly as it was.
        Assert.Equal(groceryBefore, (await Products(grocery)).Select(p => p.Sku).OrderBy(s => s).ToList());
        Assert.Equal(groceryCustomersBefore, (await Customers(grocery)).Select(c => c.Phone).OrderBy(p => p).ToList());
        Assert.DoesNotContain(await Products(grocery), p => p.Sku.StartsWith("PHM-"));
    }

    [Fact]
    public async Task An_unknown_profile_or_account_changes_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ResetDemoDataCommand>();
        await Assert.ThrowsAsync<ArgumentException>(() => command.RunAsync(SmartPosApiFactory.OtherEmail, "bakery", 7, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.RunAsync("nobody@test.local", "pharmacy", 7, default));
    }
}
