using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryMgmt.Models
{
    public class Inventory
    {
        [Key]
        public int InventoryId { get; set; }

        public int ShopId { get; set; }
        [ForeignKey("ShopId")]
        public Shop Shop { get; set; } = null!;

        public int ProductId { get; set; }
        [ForeignKey("ProductId")]
        public Product Product { get; set; } = null!;

        public int StockQuantity { get; set; }

        public int LowStockThreshold { get; set; } = 20; // Triggers alert if < 20

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}