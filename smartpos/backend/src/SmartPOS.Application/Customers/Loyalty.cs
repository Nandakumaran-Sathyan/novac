namespace SmartPOS.Application.Customers;

/// <summary>
/// How loyalty points are earned. A completed bill with a customer attached earns
/// <c>floor(bill total / spend-per-point)</c> points, where the bill total is what the customer actually
/// pays (after discount, with tax) and spend-per-point is <c>Loyalty:SpendPerPoint</c> (default 100).
/// Rounding is always down, so a part-point is never awarded: with the default, a 99.99 bill earns 0
/// points, 100.00 earns 1, 396.87 earns 3. Walk-in bills earn nothing.
/// </summary>
public static class LoyaltyRules
{
    public static int PointsEarned(decimal billTotal, decimal spendPerPoint)
    {
        if (spendPerPoint <= 0 || billTotal <= 0)
            return 0;
        return (int)Math.Floor(billTotal / spendPerPoint);
    }
}
