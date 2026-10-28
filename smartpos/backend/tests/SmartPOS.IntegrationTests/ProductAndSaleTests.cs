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
    private static async Task<ProductDto> CreateProductAsync(HttpClient user, string sku, decimal price, int stock, int lowStockThreshold = 1, string? barcode = null)
    {
        var response = await user.PostAsJsonAsync("/api/products", new
        {
            sku, barcode, name = $"Product {sku}", category = "Test Category",
            price, costPrice = Math.Round(price / 2, 2), initialStock = stock, lowStockThreshold
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(SmartPosApiFactory.Json))!;
    }

    private static async Task<int> StockOf(HttpClient client, int productId) =>
        (await client.GetFromJsonAsync<ProductDto>($"/api/products/{productId}", SmartPosApiFactory.Json))!.StockQuantity;

    [Fact]
    public async Task Duplicate_sku_is_rejected_case_insensitively()
    {
        var user = await factory.UserClientAsync();
        await CreateProductAsync(user, "DUP-1", 10m, 0);

        var response = await user.PostAsJsonAsync("/api/products", new
        {
            sku = "dup-1", name = "Another", category = "Test Category", price = 1, costPrice = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_product_returns_validation_errors()
    {
        var user = await factory.UserClientAsync();
        var response = await user.PostAsJsonAsync("/api/products", new
        {
            sku = "bad sku!", name = "", category = "", price = -5, costPrice = 1.999
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "Sku", "Name", "Category", "Price", "CostPrice" })
            Assert.Contains(field, body);
    }

    [Fact]
    public async Task Product_search_and_pagination_work()
    {
        var user = await factory.UserClientAsync();
        for (var i = 1; i <= 3; i++)
            await CreateProductAsync(user, $"PAGE-{i}", 5m, 1);

        var page = await user.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=page-&pageSize=2&page=2", SmartPosApiFactory.Json);
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Sale_prices_from_database_deducts_stock_and_records_movements()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "SALE-1", 99.99m, 10);

        var response = await user.PostAsJsonAsync("/api/sales", new
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
        Assert.Equal(283.47m, sale.TotalAmount);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.StartsWith("BILL-", sale.BillNumber);
        var item = Assert.Single(sale.Items);
        Assert.Equal(99.99m, item.UnitPrice);
        // A single line carries the whole sale discount and tax, so its total is the bill total.
        Assert.Equal(30.00m, item.DiscountAmount);
        Assert.Equal(13.50m, item.TaxAmount);
        Assert.Equal(283.47m, item.LineTotal);
        Assert.Equal(7, await StockOf(user, product.Id));

        var movements = await user.GetFromJsonAsync<PagedResult<StockMovementDto>>(
            $"/api/inventory/movements?productId={product.Id}", SmartPosApiFactory.Json);
        Assert.Collection(movements!.Items,
            m => { Assert.Equal(StockMovementType.Sale, m.Type); Assert.Equal(-3, m.QuantityChange); Assert.Equal(7, m.QuantityAfter); },
            m => Assert.Equal(StockMovementType.InitialStock, m.Type));
    }

    [Fact]
    public async Task Overselling_is_rejected_and_rolls_back_every_line()
    {
        var user = await factory.UserClientAsync();
        var plenty = await CreateProductAsync(user, "ROLL-A", 10m, 50);
        var scarce = await CreateProductAsync(user, "ROLL-B", 10m, 2);

        var response = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = plenty.Id, quantity = 5 }, new { productId = scarce.Id, quantity = 3 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(50, await StockOf(user, plenty.Id));
        Assert.Equal(2, await StockOf(user, scarce.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_quantities_are_rejected(int quantity)
    {
        var user = await factory.UserClientAsync();
        var response = await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = 1, quantity } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sale_without_items_is_rejected()
    {
        var user = await factory.UserClientAsync();
        var response = await user.PostAsJsonAsync("/api/sales", new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discount_above_the_configured_limit_is_forbidden()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "DISC-1", 10m, 5);

        var response = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, discountPercent = 60
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(5, await StockOf(user, product.Id));
    }

    [Fact]
    public async Task Historical_sale_price_survives_product_price_change()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "HIST-1", 20m, 5);
        var created = await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 1 } } });
        var sale = (await created.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;

        var update = await user.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            sku = product.Sku, name = product.Name, category = product.Category,
            price = 35m, costPrice = 10m, lowStockThreshold = 1, isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reloaded = await user.GetFromJsonAsync<SaleDto>($"/api/sales/{sale.Id}", SmartPosApiFactory.Json);
        Assert.Equal(20m, Assert.Single(reloaded!.Items).UnitPrice);

        // ...and a product with sales history cannot be deleted.
        Assert.Equal(HttpStatusCode.Conflict, (await user.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unsold_product_can_be_deleted()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "DEL-1", 5m, 3);

        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stock_adjustment_cannot_make_stock_negative()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "ADJ-1", 5m, 4);

        var tooMuch = await user.PostAsJsonAsync("/api/inventory/adjustments", new { productId = product.Id, quantityChange = -5, reason = "Damaged" });
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);

        var ok = await user.PostAsJsonAsync("/api/inventory/adjustments", new { productId = product.Id, quantityChange = -4, reason = "Damaged" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(0, await StockOf(user, product.Id));
    }

    [Fact]
    public async Task Dashboard_and_reports_reflect_recorded_sales()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "DASH-1", 10m, 5, lowStockThreshold: 4);
        var before = (await user.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary", SmartPosApiFactory.Json))!;

        await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 2 } } });

        var after = (await user.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary", SmartPosApiFactory.Json))!;
        Assert.Equal(before.TodayTransactionCount + 1, after.TodayTransactionCount);
        Assert.Equal(before.TodaySalesTotal + 21.00m, after.TodaySalesTotal); // 20 + 5% tax
        Assert.Contains(after.LowStockProducts, p => p.ProductId == product.Id); // 3 left, threshold 4

        var top = await user.GetStringAsync("/api/reports/top-products?limit=50");
        Assert.Contains("DASH-1", top);
        var daily = await user.GetAsync("/api/reports/daily-sales");
        Assert.Equal(HttpStatusCode.OK, daily.StatusCode);
    }

    [Fact]
    public async Task Cash_overpayment_returns_change_and_underpayment_is_rejected()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "PAY-1", 100m, 10);

        var paid = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "Cash", amountPaid = 200
        });
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var sale = (await paid.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(105m, sale.TotalAmount);
        Assert.Equal(200m, sale.AmountPaid);
        Assert.Equal(95m, sale.ChangeAmount);

        var short_ = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "Cash", amountPaid = 100
        });
        Assert.Equal(HttpStatusCode.BadRequest, short_.StatusCode);

        var cardOver = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "UPI", amountPaid = 200
        });
        Assert.Equal(HttpStatusCode.BadRequest, cardOver.StatusCode);
        Assert.Equal(9, await StockOf(user, product.Id)); // only the first sale went through
    }

    [Fact]
    public async Task Omitted_amount_paid_means_exact_payment()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "PAY-2", 40m, 5);
        var response = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "UPI"
        });
        var sale = (await response.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(PaymentMethod.UPI, sale.PaymentMethod);
        Assert.Equal(sale.TotalAmount, sale.AmountPaid);
        Assert.Equal(0m, sale.ChangeAmount);
    }

    [Fact]
    public async Task Line_discounts_and_taxes_add_up_to_the_sale_totals()
    {
        var user = await factory.UserClientAsync();
        var a = await CreateProductAsync(user, "SPLIT-A", 33.33m, 20);
        var b = await CreateProductAsync(user, "SPLIT-B", 10.01m, 20);
        var c = await CreateProductAsync(user, "SPLIT-C", 7.77m, 20);

        var response = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = a.Id, quantity = 3 }, new { productId = b.Id, quantity = 7 }, new { productId = c.Id, quantity = 1 } },
            // 10, not something like 12.5: SQLite stores decimals as text and compares them as strings,
            // so the DiscountPercent CHECK would wrongly reject it here (MySQL compares numerically).
            // The unit tests cover awkward percentages.
            discountPercent = 10
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var sale = (await response.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;

        Assert.Equal(sale.DiscountAmount, sale.Items.Sum(i => i.DiscountAmount));
        Assert.Equal(sale.TaxAmount, sale.Items.Sum(i => i.TaxAmount));
        Assert.Equal(sale.TotalAmount, sale.Items.Sum(i => i.LineTotal));
    }

    [Fact]
    public async Task Barcode_is_unique_within_a_shop_and_searchable()
    {
        var user = await factory.UserClientAsync();
        await CreateProductAsync(user, "BAR-1", 5m, 1, barcode: "8901234567890");

        var duplicate = await user.PostAsJsonAsync("/api/products", new
        {
            sku = "BAR-2", barcode = "8901234567890", name = "Other", category = "Test Category", price = 1, costPrice = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var found = await user.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=8901234567", SmartPosApiFactory.Json);
        Assert.Equal("BAR-1", Assert.Single(found!.Items).Sku);
    }

    [Fact]
    public async Task Sale_can_be_linked_to_a_customer_of_the_same_shop()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "CUST-1", 10m, 5);

        var created = await user.PostAsJsonAsync("/api/customers", new { name = "Asha Rao", phone = "98765 43210" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = (await created.Content.ReadFromJsonAsync<Application.Customers.CustomerDto>(SmartPosApiFactory.Json))!;
        Assert.Equal("9876543210", customer.Phone); // spaces stripped
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsJsonAsync("/api/customers", new { name = "Dup", phone = "9876543210" })).StatusCode);

        var sale = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, customerId = customer.Id
        });
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
        var dto = (await sale.Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal("Asha Rao", dto.CustomerName);

        var unknown = await user.PostAsJsonAsync("/api/sales", new
        {
            items = new[] { new { productId = product.Id, quantity = 1 } }, customerId = 999999
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Shops_cannot_see_or_touch_each_others_data()
    {
        var user = await factory.UserClientAsync();
        var other = await factory.OtherShopClientAsync();
        var mine = await CreateProductAsync(user, "TENANT-1", 10m, 5);
        var customer = await (await user.PostAsJsonAsync("/api/customers", new { name = "Mine Only", phone = "9000000001" }))
            .Content.ReadFromJsonAsync<Application.Customers.CustomerDto>(SmartPosApiFactory.Json);
        var sale = await (await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = mine.Id, quantity = 1 } } }))
            .Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json);

        // The other shop sees none of it...
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/products/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/sales/{sale!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/customers/{customer!.Id}")).StatusCode);
        var products = await other.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?pageSize=100", SmartPosApiFactory.Json);
        Assert.DoesNotContain(products!.Items, p => p.Id == mine.Id);
        var customers = await other.GetStringAsync("/api/customers");
        Assert.Contains("\"totalCount\":0", customers);
        var dashboard = await other.GetFromJsonAsync<DashboardSummaryDto>("/api/dashboard/summary", SmartPosApiFactory.Json);
        Assert.Equal(0, dashboard!.TodayTransactionCount);

        // ...cannot change it...
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/inventory/adjustments",
            new { productId = mine.Id, quantityChange = -1, reason = "Not mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/products/{mine.Id}")).StatusCode);
        var sellOthers = await other.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = mine.Id, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, sellOthers.StatusCode);
        Assert.Equal(4, await StockOf(user, mine.Id));

        // ...and both shops can use the same SKU.
        var theirs = await CreateProductAsync(other, "TENANT-1", 12m, 3);
        Assert.NotEqual(mine.Id, theirs.Id);
    }

    [Fact]
    public async Task Insights_summarise_the_period_and_stay_within_the_shop()
    {
        var user = await factory.UserClientAsync();
        var other = await factory.OtherShopClientAsync();
        var product = await CreateProductAsync(user, "INS-1", 100m, 20);
        var customer = await (await user.PostAsJsonAsync("/api/customers", new { name = "Insight Buyer", phone = "9000000777" }))
            .Content.ReadFromJsonAsync<Application.Customers.CustomerDto>(SmartPosApiFactory.Json);
        var before = (await user.GetFromJsonAsync<InsightsDto>("/api/dashboard/insights?days=7", SmartPosApiFactory.Json))!;

        await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 2 } }, paymentMethod = "UPI", customerId = customer!.Id });
        await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 1 } }, paymentMethod = "Cash" });

        var after = (await user.GetFromJsonAsync<InsightsDto>("/api/dashboard/insights?days=7", SmartPosApiFactory.Json))!;
        Assert.Equal(7, after.Daily.Count);
        Assert.Equal(before.Kpis.Bills.Current + 2, after.Kpis.Bills.Current);
        Assert.Equal(before.Kpis.ItemsSold.Current + 3, after.Kpis.ItemsSold.Current);
        Assert.Equal(before.Kpis.Revenue.Current + 315m, after.Kpis.Revenue.Current); // 3 × 100 + 5% tax
        Assert.Equal(after.Kpis.Bills.Current, after.Daily.Sum(d => d.Bills));
        Assert.Equal(after.Kpis.Bills.Current, after.Heatmap.SelectMany(row => row).Sum());
        Assert.Equal(7, after.Heatmap.Count);
        Assert.All(after.Heatmap, row => Assert.Equal(24, row.Count));
        Assert.Contains(after.Payments, p => p.Method == PaymentMethod.UPI);
        Assert.Contains(after.Categories, c => c.Category == "Test Category");
        Assert.Contains(after.TopProducts, p => p.ProductId == product.Id);
        Assert.Contains(after.TopCustomers, c => c.Name == "Insight Buyer");
        Assert.Equal(after.Kpis.Bills.Current, after.BillsWithCustomer + after.BillsWalkIn);

        // Another shop sees none of it.
        var theirs = (await other.GetFromJsonAsync<InsightsDto>("/api/dashboard/insights?days=7", SmartPosApiFactory.Json))!;
        Assert.Equal(0, theirs.Kpis.Bills.Current);
        Assert.Empty(theirs.TopCustomers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(181)]
    public async Task Insights_reject_a_period_out_of_range(int days)
    {
        var user = await factory.UserClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.GetAsync($"/api/dashboard/insights?days={days}")).StatusCode);
    }

    [Fact]
    public async Task Loyalty_points_are_earned_per_bill_total_rounded_down_and_added_to_the_balance()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "LOY-1", 100m, 50);
        var cheap = await CreateProductAsync(user, "LOY-2", 90m, 50);
        var customer = (await (await user.PostAsJsonAsync("/api/customers", new { name = "Points Buyer", phone = "9000000888" }))
            .Content.ReadFromJsonAsync<Application.Customers.CustomerDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(0, customer.LoyaltyPoints);

        // 3 × 100 + 5% tax = 315.00 → 3 points.
        var bill = (await (await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 3 } }, customerId = customer.Id }))
            .Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(315m, bill.TotalAmount);
        Assert.Equal(3, bill.LoyaltyPointsEarned);
        Assert.Equal(3, bill.CustomerLoyaltyPoints);

        // 1 × 90 + 5% = 94.50 → a part-point is not awarded.
        var small = (await (await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = cheap.Id, quantity = 1 } }, customerId = customer.Id }))
            .Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(94.5m, small.TotalAmount);
        Assert.Equal(0, small.LoyaltyPointsEarned);

        // Another 3-point bill takes the balance to 6.
        var third = (await (await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 3 } }, customerId = customer.Id }))
            .Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(6, third.CustomerLoyaltyPoints);
        Assert.Equal(6, (await user.GetFromJsonAsync<Application.Customers.CustomerDto>($"/api/customers/{customer.Id}", SmartPosApiFactory.Json))!.LoyaltyPoints);
    }

    [Fact]
    public async Task Walk_in_bills_earn_nothing_and_a_failed_sale_awards_nothing()
    {
        var user = await factory.UserClientAsync();
        var product = await CreateProductAsync(user, "LOY-3", 100m, 2);
        var customer = (await (await user.PostAsJsonAsync("/api/customers", new { name = "Walk Past", phone = "9000000889" }))
            .Content.ReadFromJsonAsync<Application.Customers.CustomerDto>(SmartPosApiFactory.Json))!;

        var walkIn = (await (await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 1 } } }))
            .Content.ReadFromJsonAsync<SaleDto>(SmartPosApiFactory.Json))!;
        Assert.Equal(0, walkIn.LoyaltyPointsEarned);
        Assert.Null(walkIn.CustomerLoyaltyPoints);

        var oversell = await user.PostAsJsonAsync("/api/sales", new { items = new[] { new { productId = product.Id, quantity = 5 } }, customerId = customer.Id });
        Assert.Equal(HttpStatusCode.Conflict, oversell.StatusCode);
        Assert.Equal(0, (await user.GetFromJsonAsync<Application.Customers.CustomerDto>($"/api/customers/{customer.Id}", SmartPosApiFactory.Json))!.LoyaltyPoints);
    }

    [Fact]
    public async Task Pos_settings_tell_the_till_the_earning_rate()
    {
        var user = await factory.UserClientAsync();
        var settings = await user.GetFromJsonAsync<PosSettingsDto>("/api/settings/pos", SmartPosApiFactory.Json);
        Assert.Equal(100m, settings!.LoyaltySpendPerPoint);
    }
}
