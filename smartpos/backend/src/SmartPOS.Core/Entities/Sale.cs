using SmartPOS.Core.Enums;

namespace SmartPOS.Core.Entities;

public class Sale
{
    public int Id { get; set; }
    /// <summary>Human-readable bill number, unique across the system.</summary>
    public string BillNumber { get; set; } = string.Empty;

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    /// <summary>The cashier (or admin) who created the bill.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Total before discounts and tax.</summary>
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Tax rate in force when the sale was made, so old invoices stay reproducible.</summary>
    public decimal TaxRatePercent { get; set; }
    public decimal TaxAmount { get; set; }
    /// <summary>Final bill amount: subtotal − discount + tax.</summary>
    public decimal TotalAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    /// <summary>Amount received from the customer.</summary>
    public decimal AmountPaid { get; set; }
    /// <summary>Change returned to the customer (cash only).</summary>
    public decimal ChangeAmount { get; set; }

    public SaleStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<SaleItem> Items { get; set; } = new();
}
