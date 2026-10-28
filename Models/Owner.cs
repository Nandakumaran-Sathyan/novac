using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryMgmt.Models
{
    public class Owner
    {
        [Key]
        public int OwnerId { get; set; }

        public int ShopId { get; set; }
        [ForeignKey("ShopId")]
        public Shop Shop { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property
        //public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    }
}