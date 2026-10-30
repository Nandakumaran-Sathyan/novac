using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;
using SmartPOS.Infrastructure.Persistence;

namespace SmartPOS.UnitTests;

/// <summary>The fabricated demo history must still obey the same rules as real data.</summary>
public class DemoHistoryTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 9, 30, 0, DateTimeKind.Utc);

    private static StoreCalendar Calendar() =>
        new(Options.Create(new StoreOptions { TimeZoneId = "Asia/Kolkata" }), new FixedTime(Now));

    private static Dictionary<Product, int> Catalogue() =>
        SampleData.Products.ToDictionary(
            p => new Product { Sku = p.Sku, Name = p.Name, Category = p.Category, Price = p.Price, CostPrice = p.CostPrice },
            p => p.Stock);

    private static DemoHistory.Result Generate(Dictionary<Product, int> target, int days = 30) =>
        DemoHistory.Generate(1, 1, target, Calendar(), Now, taxRatePercent: 5, maxDiscountPercent: 100, days);

    [Fact]
    public void Stock_ledger_runs_down_to_exactly_the_target_and_never_goes_negative()
    {
        var target = Catalogue();
        var history = Generate(target);

        Assert.NotEmpty(history.Sales);
        Assert.All(history.Movements, m => Assert.True(m.QuantityAfter >= 0));
        foreach (var (product, stock) in target)
        {
            var opening = history.OpeningStock[product];
            var movements = history.Movements.Where(m => m.Product == product).OrderBy(m => m.CreatedAt).ToList();
            Assert.Equal(stock, opening + movements.Sum(m => m.QuantityChange));
            if (movements.Count > 0)
                Assert.Equal(stock, movements[^1].QuantityAfter);
        }
    }

    [Fact]
    public void Bills_are_internally_consistent_and_never_in_the_future()
    {
        var history = Generate(Catalogue());

        Assert.All(history.Sales, s =>
        {
            Assert.True(s.CreatedAt <= Now);
            Assert.Equal(s.Subtotal - s.DiscountAmount + s.TaxAmount, s.TotalAmount);
            Assert.Equal(s.TotalAmount, s.AmountPaid - s.ChangeAmount);
            Assert.Equal(s.TotalAmount, s.Items.Sum(i => i.LineTotal));
            Assert.Equal(s.Items.Count, s.Items.Select(i => i.Product).Distinct().Count()); // one line per product
            if (s.PaymentMethod != PaymentMethod.Cash)
                Assert.Equal(0m, s.ChangeAmount);
        });
        Assert.Equal(history.Sales.Count, history.Sales.Select(s => s.BillNumber).Distinct().Count());
    }

    [Fact]
    public void Loyalty_points_follow_the_earning_rule_and_add_up_to_each_customers_balance()
    {
        var history = DemoHistory.Generate(1, 1, Catalogue(), Calendar(), Now, 5, 100, 60, loyaltySpendPerPoint: 100);

        Assert.All(history.Sales, s =>
            Assert.Equal(s.Customer is null ? 0 : (int)Math.Floor(s.TotalAmount / 100m), s.LoyaltyPointsEarned));
        Assert.Contains(history.Sales, s => s.LoyaltyPointsEarned > 0);
        foreach (var customer in history.Customers)
            Assert.Equal(history.Sales.Where(s => s.Customer == customer).Sum(s => s.LoyaltyPointsEarned), customer.LoyaltyPoints);
    }

    [Fact]
    public void Two_shops_never_share_a_bill_number()
    {
        var first = DemoHistory.Generate(1, 1, Catalogue(), Calendar(), Now, 5, 100, 30);
        var second = DemoHistory.Generate(2, 2, Catalogue(), Calendar(), Now, 5, 100, 30);
        Assert.Empty(first.Sales.Select(s => s.BillNumber).Intersect(second.Sales.Select(s => s.BillNumber)));
    }

    [Fact]
    public void Same_input_gives_the_same_history()
    {
        var a = Generate(Catalogue());
        var b = Generate(Catalogue());
        Assert.Equal(a.Sales.Count, b.Sales.Count);
        Assert.Equal(a.Sales.Sum(s => s.TotalAmount), b.Sales.Sum(s => s.TotalAmount));
    }

    [Fact]
    public void Weekends_are_busier_than_weekdays_and_customers_are_a_minority_of_bills()
    {
        var history = Generate(Catalogue(), days: 84);
        var calendar = Calendar();
        double PerDay(Func<DayOfWeek, bool> pick) => history.Sales
            .Count(s => pick(calendar.ToStoreTime(s.CreatedAt).DayOfWeek)) /
            (double)Enumerable.Range(0, 84).Count(d => pick(calendar.ToStoreDate(Now).AddDays(-d).DayOfWeek));

        Assert.True(PerDay(d => d == DayOfWeek.Saturday) > PerDay(d => d == DayOfWeek.Tuesday));
        var share = history.Sales.Count(s => s.Customer != null) / (double)history.Sales.Count;
        Assert.InRange(share, 0.25, 0.5);
    }

    private static Dictionary<Product, int> Catalogue(SampleData.DemoProfile profile) =>
        profile.Products.ToDictionary(
            p => new Product { Sku = p.Sku, Name = p.Name, Category = p.Category, Price = p.Price, CostPrice = p.CostPrice },
            p => p.Stock);

    [Fact]
    public void The_pharmacy_profile_sells_only_pharmacy_products_to_its_own_customers()
    {
        var target = Catalogue(SampleData.Pharmacy);
        var history = DemoHistory.Generate(3, 3, target, Calendar(), Now, 5, 100, 45, profile: SampleData.Pharmacy);

        Assert.NotEmpty(history.Sales);
        Assert.All(history.Sales.SelectMany(s => s.Items), i => Assert.StartsWith("PHM-", i.Sku));
        var groceryNames = SampleData.Grocery.Customers.Select(c => c.Name).ToHashSet();
        var groceryPhones = SampleData.Grocery.Customers.Select(c => c.Phone).ToHashSet();
        Assert.All(history.Customers, c => Assert.DoesNotContain(c.Name, groceryNames));
        Assert.All(history.Customers, c => Assert.DoesNotContain(c.Phone, groceryPhones));

        // Same ledger guarantee as the grocery profile.
        foreach (var (product, stock) in target)
        {
            var movements = history.Movements.Where(m => m.Product == product).OrderBy(m => m.CreatedAt).ToList();
            Assert.Equal(stock, history.OpeningStock[product] + movements.Sum(m => m.QuantityChange));
        }
        Assert.All(history.Movements, m => Assert.True(m.QuantityAfter >= 0));
    }

    [Fact]
    public void The_profiles_are_genuinely_different_shops()
    {
        Assert.Empty(SampleData.Pharmacy.Products.Select(p => p.Sku).Intersect(SampleData.Grocery.Products.Select(p => p.Sku)));
        Assert.Empty(SampleData.Pharmacy.Products.Select(p => p.Category).Intersect(SampleData.Grocery.Products.Select(p => p.Category))
            .Except(["Personal Care"]));                                                     // the only shared category name
        Assert.Contains(SampleData.Pharmacy.Products, p => p.ShelfLifeDays < 0);            // something already expired
        Assert.Contains(SampleData.Pharmacy.Products, p => p.ShelfLifeDays is >= 0 and <= 30); // something expiring soon
        Assert.Contains(SampleData.Pharmacy.Products, p => p.Stock <= p.LowStockThreshold);  // something low
        Assert.Same(SampleData.Pharmacy, SampleData.ProfileNamed(" Pharmacy "));
        Assert.Throws<ArgumentException>(() => SampleData.ProfileNamed("bakery"));
    }

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
