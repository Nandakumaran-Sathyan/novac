using SmartPOS.Core.Enums;

namespace SmartPOS.Core.Entities;

/// <summary>Append-only ledger of every stock change.</summary>
public class StockMovement
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Positive for stock in, negative for stock out.</summary>
    public int QuantityChange { get; set; }
    public int QuantityAfter { get; set; }
    public StockMovementType Type { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }

    /// <summary>Who made the change. Null for system actions such as sample-data seeding.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public DateTime CreatedAt { get; set; }
}
