using SmartPOS.Application.Common;
using SmartPOS.Application.Inventory;

namespace SmartPOS.UnitTests;

public class StockRulesTests
{
    [Theory]
    [InlineData(10, 1)]
    [InlineData(10, 10)] // selling the last unit is fine
    public void Available_stock_passes(int onHand, int requested) =>
        StockRules.EnsureAvailable("Milk", onHand, requested);

    [Fact]
    public void Requesting_more_than_on_hand_is_a_conflict()
    {
        var ex = Assert.Throws<ConflictException>(() => StockRules.EnsureAvailable("Milk", 3, 4));
        Assert.Contains("requested 4, available 3", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_request_is_rejected(int requested) =>
        Assert.Throws<BusinessRuleException>(() => StockRules.EnsureAvailable("Milk", 10, requested));

    [Fact]
    public void Adjustment_may_bring_stock_to_exactly_zero() =>
        StockRules.EnsureValidAdjustment(onHand: 5, quantityChange: -5);

    [Fact]
    public void Adjustment_below_zero_is_a_conflict() =>
        Assert.Throws<ConflictException>(() => StockRules.EnsureValidAdjustment(onHand: 5, quantityChange: -6));

    [Fact]
    public void Zero_adjustment_is_rejected() =>
        Assert.Throws<BusinessRuleException>(() => StockRules.EnsureValidAdjustment(5, 0));

    [Theory]
    [InlineData(0, 5, StockStatus.OutOfStock)]
    [InlineData(5, 5, StockStatus.Low)]      // at reorder level counts as low
    [InlineData(6, 5, StockStatus.InStock)]
    [InlineData(0, 0, StockStatus.OutOfStock)]
    public void Status_reflects_reorder_level(int onHand, int reorderLevel, StockStatus expected) =>
        Assert.Equal(expected, StockRules.GetStatus(onHand, reorderLevel));
}
