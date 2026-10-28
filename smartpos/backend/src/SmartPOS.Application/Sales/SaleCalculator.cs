using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Sales;

public sealed record SaleLineInput(decimal UnitPrice, int Quantity);

/// <summary>
/// Per-line lists run in the same order as the input lines. A line's total is its subtotal (unit
/// price × quantity) minus its share of the discount plus its share of the tax, and the line totals
/// add up to exactly <see cref="Total"/>.
/// </summary>
public sealed record SaleTotals(
    IReadOnlyList<decimal> LineSubtotals,
    IReadOnlyList<decimal> LineDiscounts,
    IReadOnlyList<decimal> LineTaxes,
    IReadOnlyList<decimal> LineTotals,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total);

/// <summary>
/// The single source of truth for sale arithmetic. The Angular cart mirrors these rules for its
/// live preview, but the server always recalculates from database prices.
/// <list type="number">
/// <item>Line subtotal = unit price × quantity, rounded to 2 dp.</item>
/// <item>Subtotal = sum of line subtotals.</item>
/// <item>Discount = subtotal × discount% (sale-level), rounded.</item>
/// <item>Tax = (subtotal − discount) × tax%, rounded.</item>
/// <item>Total = subtotal − discount + tax.</item>
/// <item>The discount and tax are then split across lines in proportion to their value, to the cent
/// (largest remainder), so the lines always add up to exactly the sale figures.</item>
/// </list>
/// Rounding is half away from zero, which matches what customers expect on a receipt.
/// </summary>
public static class SaleCalculator
{
    /// <summary>Largest amount the DECIMAL(12,2) money columns can hold.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    public static SaleTotals Calculate(IReadOnlyList<SaleLineInput> lines, decimal discountPercent, decimal taxRatePercent)
    {
        if (lines.Count == 0)
            throw new BusinessRuleException("A sale must contain at least one item.");
        if (discountPercent is < 0 or > 100)
            throw new BusinessRuleException("Discount must be between 0 and 100 percent.");
        if (taxRatePercent is < 0 or > 100)
            throw new BusinessRuleException("Tax rate must be between 0 and 100 percent.");

        var lineSubtotals = new List<decimal>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new BusinessRuleException("Item quantities must be greater than zero.");
            if (line.UnitPrice < 0)
                throw new BusinessRuleException("Unit prices cannot be negative.");
            lineSubtotals.Add(Round(line.UnitPrice * line.Quantity));
        }

        var subtotal = lineSubtotals.Sum();
        if (subtotal > MaxAmount)
            throw new BusinessRuleException("This sale is too large to record.");
        var discount = Round(subtotal * discountPercent / 100m);
        var tax = Round((subtotal - discount) * taxRatePercent / 100m);

        var lineDiscounts = Allocate(discount, lineSubtotals);
        var taxable = lineSubtotals.Select((value, i) => value - lineDiscounts[i]).ToList();
        var lineTaxes = Allocate(tax, taxable);
        var lineTotals = taxable.Select((value, i) => value + lineTaxes[i]).ToList();

        return new SaleTotals(lineSubtotals, lineDiscounts, lineTaxes, lineTotals, subtotal, discount, tax, subtotal - discount + tax);
    }

    /// <summary>
    /// Works out what was paid and the change due. Cash may be over-tendered (the difference is
    /// change); every other method must be paid exactly. Omitting the amount means "exactly the total".
    /// </summary>
    public static (decimal AmountPaid, decimal ChangeAmount) Settle(decimal total, PaymentMethod method, decimal? amountPaid)
    {
        var paid = amountPaid ?? total;
        if (paid < total)
            throw new BusinessRuleException($"Amount paid ({paid:0.00}) is less than the total ({total:0.00}).");
        if (paid > total && method != PaymentMethod.Cash)
            throw new BusinessRuleException("Change can only be given for cash payments.");
        return (paid, paid - total);
    }

    /// <summary>
    /// Splits <paramref name="amount"/> across <paramref name="weights"/> in proportion, in whole cents,
    /// giving the leftover cents to the largest fractional remainders. The parts always sum to the amount.
    /// </summary>
    internal static List<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        var shares = new decimal[weights.Count];
        var weightTotal = weights.Sum();
        if (amount == 0 || weightTotal == 0)
            return shares.ToList();

        var remaining = (long)(amount * 100m);
        var floors = new long[weights.Count];
        var remainders = new decimal[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = amount * 100m * weights[i] / weightTotal;
            floors[i] = (long)Math.Floor(exact);
            remainders[i] = exact - floors[i];
            remaining -= floors[i];
        }

        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take((int)remaining))
            floors[i]++;

        return floors.Select(c => c / 100m).ToList();
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
