using SmartPOS.Core.Enums;

namespace SmartPOS.Core.Entities;

public class Sale
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;

    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Tax rate in force when the sale was made, so old invoices stay reproducible.</summary>
    public decimal TaxRatePercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<SaleItem> Items { get; set; } = new();
}
