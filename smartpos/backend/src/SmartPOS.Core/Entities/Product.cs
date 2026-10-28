namespace SmartPOS.Core.Entities;

public class Product
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    /// <summary>Internal code created by the owner. Unique within a shop, stored upper-case.</summary>
    public string Sku { get; set; } = string.Empty;
    /// <summary>Manufacturer's scannable barcode. Unique within a shop when present.</summary>
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public decimal CostPrice { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Inactive products are hidden from the POS but keep their sales history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Inventory? Inventory { get; set; }
}
