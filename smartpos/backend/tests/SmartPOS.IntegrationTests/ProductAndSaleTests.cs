using System.Net;
using System.Net.Http.Json;
using SmartPOS.Application.Common;
using SmartPOS.Application.Dashboard;
using SmartPOS.Application.Inventory;
using SmartPOS.Application.Products;
using SmartPOS.Application.Sales;
using SmartPOS.Core.Enums;

namespace SmartPOS.IntegrationTests;

public class ProductAndSaleTests(SmartPosApiFactory factory) : IClassFixture<SmartPosApiFactory>
{
    private async Task<ProductDto> CreateProductAsync(HttpClient admin, string sku, decimal price, int stock, int reorderLevel = 1)
    {
        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            sku, name = $"Product {sku}", categoryId = factory.CategoryId,
            sellingPrice = price, costPrice = Math.Round(price / 2, 2), initialStock = stock, reorderLevel
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(SmartPosApiFactory.Json))!;
    }

    private static async Task<int> StockOf(HttpClient client, int productId) =>
        (await client.GetFromJsonAsync<ProductDto>($"/api/products/{productId}", SmartPosApiFactory.Json))!.StockQuantity;

    [Fact]
    public async Task Duplicate_sku_is_rejected_case_insensitively()
    {
        var admin = await factory.AdminClientAsync();
        await CreateProductAsync(admin, "DUP-1", 10m, 0);

        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            sku = "dup-1", name = "Another", categoryId = factory.CategoryId, sellingPrice = 1, costPrice = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_product_returns_validation_errors()
    {
        var admin = await factory.AdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/products", new
        {
            sku = "bad sku!", name = "", categoryId = 0, sellingPrice = -5, costPrice = 1.999
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "Sku", "Name", "CategoryId", "SellingPrice", "CostPrice" })
            Assert.Contains(field, body);
    }

    [Fact]
    public async Task Product_search_and_pagination_work()
    {
        var admin = await factory.AdminClientAsync();
        for (var i = 1; i <= 3; i++)
            await CreateProductAsync(admin, $"PAGE-{i}", 5m, 1);

        var page = await admin.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=page-&pageSize=2&page=2", SmartPosApiFactory.Json);
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Sale_prices_from_database_deducts_stock_and_records_movements()
    {
        var admin = await factory.AdminClientAsync();
        var cashier = await factory.CashierClientAsync();
        var product = await CreateProductAsync(admin, "SALE-1", 99.99m, 10);

        var response = await cashier.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 3 } },
            discountPercent = 10,
            paymentMethod = "Cash"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sale = (await response.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(299.97m, sale.Subtotal);
        Assert.Equal(30.00m, sale.DiscountAmount);   // 29.997
        Assert.Equal(13.50m, sale.TaxAmount);        // 5% of 269.97 = 13.4985
        Assert.Equal(283.47m, sale.Total);
        Assert.Equal(99.99m, Assert.Single(sale.Items).UnitPrice);
        Assert.Equal(7, await StockOf(admin, product.Id));

        var movements = await admin.GetFromJsonAsync<PagedResult<StockMovementDto>>(
            $"/api/inventory/movements?productId={product.Id}", SmartPosApiFactory.Json);
        Assert.Collection(movements!.Items,
            m => { Assert.Equal(StockMovementType.Sale, m.Type); Assert.Equal(-3, m.QuantityChange); Assert.Equal(7, m.QuantityAfter); },
            m => Assert.Equal(StockMovementType.InitialStock, m.Type));
    }

    [Fact]
    public async Task Overselling_is_rejected_and_rolls_back_every_line()
    {
        var admin = await factory.AdminClientAsync();
        var cashier = await factory.CashierClientAsync();
        var plenty = await CreateProductAsync(admin, "ROLL-A", 10m, 50);
        var scarce = await CreateProductAsync(admin, "ROLL-B", 10m, 2);

        var response = await cashier.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = plenty.Id, quantity = 5 }, new { productId = scarce.Id, quantity = 3 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(50, await StockOf(admin, plenty.Id));
        Assert.Equal(2, await StockOf(admin, scarce.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_quantities_are_rejected(int quantity)
    {
        var cashier = await factory.CashierClientAsync();
        var response = await cashier.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = 1, quantity } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sale_without_items_is_rejected()
    {
        var cashier = await factory.CashierClientAsync();
        var response = await cashier.PostAsJsonAsync("/api/sales", new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_discount_above_limit_is_forbidden()
    {
        var admin = await factory.AdminClientAsync();
        var cashier = await factory.CashierClientAsync();
        var product = await CreateProductAsync(admin, "DISC-1", 10m, 5);

        var response = await cashier.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, discountPercent = 25
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(5, await StockOf(admin, product.Id));
    }

    [Fact]
    public async Task Historical_sale_price_survives_product_price_change()
    {
        var admin = await factory.AdminClientAsync();
        var product = await CreateProductAsync(admin, "HIST-1", 20m, 5);
        var created = await admin.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 1 } } });
        var sale = (await created.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;

        var update = await admin.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            sku = product.Sku, name = product.Name, categoryId = product.CategoryId,
            sellingPrice = 35m, costPrice = 10m, reorderLevel = 1, isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reloaded = await admin.GetFromJsonAsync<SaleDto>($"/api/sales/{sale.Id}", SmartPosApiFactory.Json);
        Assert.Equal(20m, Assert.Single(reloaded!.Items).UnitPrice);

        // ...and a product with sales history cannot be deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unsold_product_can_be_deleted()
    {
        var admin = await factory.AdminClientAsync();
        var product = await CreateProductAsync(admin, "DEL-1", 5m, 3);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stock_adjustment_cannot_make_stock_negative()
    {
        var admin = await factory.AdminClientAsync();
        var product = await CreateProductAsync(admin, "ADJ-1", 5m, 4);

        var tooMuch = await admin.PostAsJsonAsync("/api/inventory/adjustments", new { productId = product.Id, quantityChange = -5, reason = "Damaged" });
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);

        var ok = await admin.PostAsJsonAsync("/api/inventory/adjustments", new { productId = product.Id, quantityChange = -4, reason = "Damaged" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(0, await StockOf(admin, product.Id));
    }

    [Fact]
    public async Task Cashier_only_sees_own_sales()
    {
        var admin = await factory.AdminClientAsync();
        var cashier = await factory.CashierClientAsync();
        var product = await CreateProductAsync(admin, "VIS-1", 5m, 5);
        var adminSale = await admin.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 1 } } });
        var sale = (await adminSale.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
        var list = await cashier.GetFromJsonAsync<PagedResult<SaleSummaryDto>>("/api/sales?pageSize=100", SmartPosApiFactory.Json);
        Assert.DoesNotContain(list!.Items, s => s.Id == sale.Id);
    }

    [Fact]
    public async Task Dashboard_and_reports_reflect_recorded_sales()
    {
        var admin = await factory.AdminClientAsync();
        var product = await CreateProductAsync(admin, "DASH-1", 10m, 5, reorderLevel: 4);
        var before = (await admin.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary", SmartPosApiFactory.Json))!;

        await admin.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 2 } } });

        var after = (await admin.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary", SmartPosApiFactory.Json))!;
        Assert.Equal(before.TodayTransactionCount + 1, after.TodayTransactionCount);
        Assert.Equal(before.TodaySalesTotal + 21.00m, after.TodaySalesTotal); // 20 + 5% tax
        Assert.Contains(after.LowStockProducts, p => p.ProductId == product.Id); // 3 left, reorder level 4

        var top = await admin.GetStringAsync("/api/reports/top-products?limit=50");
        Assert.Contains("DASH-1", top);
        var daily = await admin.GetAsync("/api/reports/daily-sales");
        Assert.Equal(HttpStatusCode.OK, daily.StatusCode);
    }
}
