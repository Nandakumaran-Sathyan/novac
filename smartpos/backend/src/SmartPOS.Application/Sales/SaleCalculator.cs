using SmartPOS.Application.Common;

namespace SmartPOS.Application.Sales;

public sealed record SaleLineInput(decimal UnitPrice, int Quantity);

public sealed record SaleTotals(
    IReadOnlyList<decimal> LineTotals,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total);

/// <summary>
/// The single source of truth for sale arithmetic. The Angular cart mirrors these rules for its
/// live preview, but the server always recalculates from database prices.
/// <list type="number">
/// <item>Line total = unit price × quantity, rounded to 2 dp.</item>
/// <item>Subtotal = sum of line totals.</item>
/// <item>Discount = subtotal × discount% (sale-level), rounded.</item>
/// <item>Tax = (subtotal − discount) × tax%, rounded.</item>
/// <item>Total = subtotal − discount + tax.</item>
/// </list>
/// Rounding is half away from zero, which matches what customers expect on a receipt.
/// </summary>
public static class SaleCalculator
{
    public static SaleTotals Calculate(IReadOnlyList<SaleLineInput> lines, decimal discountPercent, decimal taxRatePercent)
    {
        if (lines.Count == 0)
            throw new BusinessRuleException("A sale must contain at least one item.");
        if (discountPercent is < 0 or > 100)
            throw new BusinessRuleException("Discount must be between 0 and 100 percent.");
        if (taxRatePercent is < 0 or > 100)
            throw new BusinessRuleException("Tax rate must be between 0 and 100 percent.");

        var lineTotals = new List<decimal>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new BusinessRuleException("Item quantities must be greater than zero.");
            if (line.UnitPrice < 0)
                throw new BusinessRuleException("Unit prices cannot be negative.");
            lineTotals.Add(Round(line.UnitPrice * line.Quantity));
        }

        var subtotal = lineTotals.Sum();
        var discount = Round(subtotal * discountPercent / 100m);
        var tax = Round((subtotal - discount) * taxRatePercent / 100m);
        return new SaleTotals(lineTotals, subtotal, discount, tax, subtotal - discount + tax);
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
