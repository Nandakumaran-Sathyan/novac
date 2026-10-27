namespace SmartPOS.Core.Entities;

public class Product
{
    public int Id { get; set; }
    /// <summary>Stock-keeping unit. Unique, stored upper-case.</summary>
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public decimal SellingPrice { get; set; }
    public decimal CostPrice { get; set; }

    /// <summary>
    /// Units on hand. Changed only through sales and inventory adjustments (never by a product edit),
    /// so every change has a matching <see cref="StockMovement"/>.
    /// </summary>
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }

    /// <summary>Inactive products are hidden from the POS but keep their sales history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsLowStock => StockQuantity <= ReorderLevel;
}
