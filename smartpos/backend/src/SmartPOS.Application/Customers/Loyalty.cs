namespace SmartPOS.Application.Customers;

/// <summary>
/// How loyalty points are earned and redeemed.
/// <para>
/// Earning: a completed bill with a customer attached earns <c>floor(bill total / spend-per-point)</c> points,
/// where the bill total is what the customer actually pays (after the discount and any points redeemed) and
/// spend-per-point is <c>Loyalty:SpendPerPoint</c> (default 100). Rounding is always down, so a part-point is
/// never awarded: with the default, a 99.99 bill earns 0 points, 100.00 earns 1, 396.87 earns 3. Walk-in bills
/// earn nothing.
/// </para>
/// <para>
/// Redeeming: whole points only, each worth <c>Loyalty:PointValue</c> (default 1.00). They come off the bill
/// after the percentage discount, never below zero, and never more than the customer's balance.
/// </para>
/// The balance is always the sum of earned minus redeemed over the customer's bills.
/// </summary>
public static class LoyaltyRules
{
    public static int PointsEarned(decimal billTotal, decimal spendPerPoint)
    {
        if (spendPerPoint <= 0 || billTotal <= 0)
            return 0;
        return (int)Math.Floor(billTotal / spendPerPoint);
    }

    public static decimal RedemptionValue(int points, decimal pointValue) =>
        Math.Round(points * pointValue, 2, MidpointRounding.AwayFromZero);

    /// <summary>Most points that can be used on a bill: limited by the balance and by the amount still due.</summary>
    public static int MaxRedeemable(int balance, decimal amountDue, decimal pointValue)
    {
        if (balance <= 0 || amountDue <= 0 || pointValue <= 0)
            return 0;
        return (int)Math.Min(balance, Math.Floor(amountDue / pointValue));
    }
}
