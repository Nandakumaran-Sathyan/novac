namespace SmartPOS.Core.Entities;

/// <summary>
/// A store. Every other business table carries a ShopId, and the API scopes every query to the
/// signed-in user's shop, so shops never see each other's data. Each shop has exactly one user (its owner).
/// </summary>
public class Shop
{
    public int Id { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
