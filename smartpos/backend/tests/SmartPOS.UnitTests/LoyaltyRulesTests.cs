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

    [Theory]
    [InlineData(50, 1, 50.00)]
    [InlineData(7, 0.25, 1.75)]
    [InlineData(3, 0.33, 0.99)]
    [InlineData(0, 1, 0)]
    public void Redemption_value_is_points_times_the_point_value(int points, decimal pointValue, decimal expected) =>
        Assert.Equal(expected, LoyaltyRules.RedemptionValue(points, pointValue));

    [Theory]
    [InlineData(120, 300, 1, 120)]      // the balance is the limit
    [InlineData(500, 216.87, 1, 216)]   // the amount due is the limit, in whole points
    [InlineData(500, 10, 0.25, 40)]
    [InlineData(0, 300, 1, 0)]
    [InlineData(100, 0, 1, 0)]          // a fully discounted bill has nothing left to pay with points
    public void At_most_the_balance_and_never_more_than_the_amount_due(int balance, decimal due, decimal pointValue, int expected) =>
        Assert.Equal(expected, LoyaltyRules.MaxRedeemable(balance, due, pointValue));
}
