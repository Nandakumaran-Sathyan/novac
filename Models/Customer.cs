using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryMgmt.Models
{
    public class Customer
    {
        [Key]
        public int CustomerId { get; set; }

        public int ShopId { get; set; }
        [ForeignKey("ShopId")]
        public Shop Shop { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        public int LoyaltyPoints { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        //public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    }
}