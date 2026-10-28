namespace SmartPOS.Core.Entities;

/// <summary>
/// Stock on hand for one product in one shop. Changed only through sales and inventory
/// adjustments (never by a product edit), so every change has a matching <see cref="StockMovement"/>.
/// </summary>
public class Inventory
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int StockQuantity { get; set; }

    /// <summary>Stock at or below this level counts as low.</summary>
    public int LowStockThreshold { get; set; } = 20;

    public DateTime LastUpdated { get; set; }

    public bool IsLowStock => StockQuantity <= LowStockThreshold;
}
