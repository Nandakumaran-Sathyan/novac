using SmartPOS.Application.Common;
using SmartPOS.Application.Sales;
using SmartPOS.Core.Enums;

namespace SmartPOS.UnitTests;

public class SaleCalculatorTests
{
    [Fact]
    public void Calculates_subtotal_discount_tax_and_total()
    {
        // 3 × 99.99 = 299.97, 3 × 40 = 120 → subtotal 419.97
        // 10% discount = 41.997 → 42.00; taxable 377.97; 5% tax = 18.8985 → 18.90
        var totals = SaleCalculator.Calculate([new(99.99m, 3), new(40m, 3)], discountPercent: 10, taxRatePercent: 5);

        Assert.Equal([299.97m, 120m], totals.LineSubtotals);
        Assert.Equal(419.97m, totals.Subtotal);
        Assert.Equal(42.00m, totals.DiscountAmount);
        Assert.Equal(18.90m, totals.TaxAmount);
        Assert.Equal(396.87m, totals.Total);

        // Discount and tax are shared between the lines in proportion to their value:
        // line 1 = 299.97 - 30.00 + 13.50, line 2 = 120.00 - 12.00 + 5.40.
        Assert.Equal([30.00m, 12.00m], totals.LineDiscounts);
        Assert.Equal([13.50m, 5.40m], totals.LineTaxes);
        Assert.Equal([283.47m, 113.40m], totals.LineTotals);
    }

    [Fact]
    public void Line_amounts_always_add_up_to_the_sale_amounts()
    {
        // Awkward numbers on purpose: thirds, odd cents, many lines.
        var lines = new SaleLineInput[] { new(33.33m, 3), new(10.01m, 7), new(7.77m, 1), new(0.01m, 1), new(1999.99m, 2) };
        foreach (var (discount, tax) in new[] { (0m, 0m), (12.5m, 5m), (33.33m, 18m), (100m, 18m), (0.5m, 12.5m) })
        {
            var totals = SaleCalculator.Calculate(lines, discount, tax);
            Assert.Equal(totals.DiscountAmount, totals.LineDiscounts.Sum());
            Assert.Equal(totals.TaxAmount, totals.LineTaxes.Sum());
            Assert.Equal(totals.Total, totals.LineTotals.Sum());
            Assert.All(totals.LineTotals, t => Assert.True(t >= 0));
        }
    }

    [Fact]
    public void Allocate_splits_to_the_cent_and_never_goes_negative()
    {
        // 0.02 over four equal weights: naive rounding would give 0.01 + 0.01 + 0.01 + (-0.01).
        var shares = SaleCalculator.Allocate(0.02m, [10m, 10m, 10m, 10m]);
        Assert.Equal(0.02m, shares.Sum());
        Assert.All(shares, s => Assert.True(s >= 0));

        Assert.Equal([0m, 0m], SaleCalculator.Allocate(0m, [5m, 5m]));
        Assert.Equal([0m, 0m], SaleCalculator.Allocate(1m, [0m, 0m]));
    }

    [Fact]
    public void Too_large_a_sale_is_rejected() =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Calculate([new(9_999_999m, 10_000), new(9_999_999m, 10_000)], 0, 0));

    [Fact]
    public void Cash_may_be_over_tendered_and_the_difference_is_change()
    {
        Assert.Equal((200m, 95m), SaleCalculator.Settle(105m, PaymentMethod.Cash, 200m));
        Assert.Equal((105m, 0m), SaleCalculator.Settle(105m, PaymentMethod.Cash, null));
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.UPI)]
    [InlineData(PaymentMethod.Other)]
    public void Non_cash_must_be_paid_exactly(PaymentMethod method)
    {
        Assert.Equal((50m, 0m), SaleCalculator.Settle(50m, method, 50m));
        Assert.Equal((50m, 0m), SaleCalculator.Settle(50m, method, null));
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Settle(50m, method, 60m));
    }

    [Fact]
    public void Paying_less_than_the_total_is_rejected() =>
        Assert.Throws<BusinessRuleException>(() => SaleCalculator.Settle(50m, PaymentMethod.Cash, 49.99m));

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
