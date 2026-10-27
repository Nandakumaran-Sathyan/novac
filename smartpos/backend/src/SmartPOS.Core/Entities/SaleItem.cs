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
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
