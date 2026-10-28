using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryMgmt.Models
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        public int ShopId { get; set; }
        [ForeignKey("ShopId")]
        public Shop Shop { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string SKU { get; set; } = string.Empty; // Internal code created by the owner

        [MaxLength(50)]
        public string? Barcode { get; set; } // Manufacturer's scannable barcode

        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Inventory? Inventory { get; set; }
        //public ICollection<BillItem> BillItems { get; set; } = new List<BillItem>();
    }
}