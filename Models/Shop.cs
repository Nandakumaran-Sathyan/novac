using System.ComponentModel.DataAnnotations;

namespace InventoryMgmt.Models
{
    public class Shop
    {
        [Key]
        public int ShopId { get; set; }

        [Required]
        [MaxLength(150)]
        public string ShopName { get; set; } = string.Empty;

        [MaxLength(250)]
        public string Address { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Owner Owner { get; set; } = null!;
        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
        public ICollection<Customer> Customers { get; set; } = new List<Customer>();
        //public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    }
}