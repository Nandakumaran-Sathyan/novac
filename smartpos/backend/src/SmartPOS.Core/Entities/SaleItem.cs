namespace SmartPOS.Core.Entities;

/// <summary>
/// A line on a sale. Name, SKU, price and cost are copied from the product at the time of sale,
/// so later product edits never change historical invoices or reports.
/// </summary>
public class SaleItem
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>What the product cost us at the time of sale (for profit reports).</summary>
    public decimal UnitCost { get; set; }
    /// <summary>This line's share of the sale-level discount.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Tax on this line, charged after its discount.</summary>
    public decimal TaxAmount { get; set; }
    /// <summary>Final amount for this line: unit price × quantity − discount + tax.</summary>
    public decimal LineTotal { get; set; }
}
