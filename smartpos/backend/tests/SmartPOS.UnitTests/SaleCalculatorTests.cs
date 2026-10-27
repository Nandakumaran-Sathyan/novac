using SmartPOS.Application.Common;
using SmartPOS.Application.Sales;

namespace SmartPOS.UnitTests;

public class SaleCalculatorTests
{
    [Fact]
    public void Calculates_subtotal_discount_tax_and_total()
    {
        // 3 × 99.99 = 299.97, 3 × 40 = 120 → subtotal 419.97
        // 10% discount = 41.997 → 42.00; taxable 377.97; 5% tax = 18.8985 → 18.90
        var totals = SaleCalculator.Calculate([new(99.99m, 3), new(40m, 3)], discountPercent: 10, taxRatePercent: 5);

        Assert.Equal([299.97m, 120m], totals.LineTotals);
        Assert.Equal(419.97m, totals.Subtotal);
        Assert.Equal(42.00m, totals.DiscountAmount);
        Assert.Equal(18.90m, totals.TaxAmount);
        Assert.Equal(396.87m, totals.Total);
    }

    [Fact]
    public void No_discount_and_no_tax_total_equals_subtotal()
    {
        var totals = SaleCalculator.Calculate([new(12.50m, 2)], 0, 0);

        Assert.Equal(25m, totals.Subtotal);
        Assert.Equal(0m, totals.DiscountAmount);
        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(25m, totals.Total);
    }

    [Fact]
    public void Tax_is_applied_after_discount()
    {
        var totals = SaleCalculator.Calculate([new(100m, 1)], discountPercent: 20, taxRatePercent: 10);

        Assert.Equal(20m, totals.DiscountAmount);
        Assert.Equal(8m, totals.TaxAmount); // 10% of 80, not of 100
        Assert.Equal(88m, totals.Total);
    }

    [Fact]
    public void Full_discount_gives_zero_total()
    {
        var totals = SaleCalculator.Calculate([new(59.99m, 2)], 100, 18);
        Assert.Equal(0m, totals.Total);
    }

    [Theory]
    [InlineData(0.125, 0.13)]   // midpoint rounds away from zero, not to even
    [InlineData(0.135, 0.14)]
    [InlineData(2.004, 2.00)]
    public void Rounds_half_away_from_zero(decimal value, decimal expected) =>
        Assert.Equal(expected, SaleCalculator.Round(value));

    [Fact]
    public void Empty_sale_is_rejected() =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate([], 0, 0));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_quantity_is_rejected(int quantity) =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate([new(10m, quantity)], 0, 0));

    [Fact]
    public void Negative_price_is_rejected() =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate([new(-1m, 1)], 0, 0));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Discount_outside_0_to_100_is_rejected(decimal discount) =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate([new(10m, 1)], discount, 0));
}
