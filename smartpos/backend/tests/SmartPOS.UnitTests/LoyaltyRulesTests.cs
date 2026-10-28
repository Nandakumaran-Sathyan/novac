using SmartPOS.Application.Customers;

namespace SmartPOS.UnitTests;

public class LoyaltyRulesTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(99.99, 100, 0)]    // a part-point is never awarded
    [InlineData(100.00, 100, 1)]
    [InlineData(396.87, 100, 3)]
    [InlineData(1_000, 100, 10)]
    [InlineData(250, 50, 5)]       // a different rate: 1 point per 50
    [InlineData(250, 0, 0)]        // a nonsense rate earns nothing rather than dividing by zero
    [InlineData(-5, 100, 0)]
    public void Points_are_the_bill_total_divided_by_the_spend_per_point_rounded_down(decimal total, decimal spendPerPoint, int expected) =>
        Assert.Equal(expected, LoyaltyRules.PointsEarned(total, spendPerPoint));
}
