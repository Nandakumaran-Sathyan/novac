namespace SmartPOS.Core.Entities;

public class Customer
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    /// <summary>Unique within a shop; used to look the customer up at the till.</summary>
    public string Phone { get; set; } = string.Empty;
    public int LoyaltyPoints { get; set; }
    public DateTime CreatedAt { get; set; }
}
