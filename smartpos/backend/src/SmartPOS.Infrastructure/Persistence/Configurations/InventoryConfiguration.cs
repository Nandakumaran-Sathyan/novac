using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("Inventories", t =>
        {
            // Last line of defence: even a bug elsewhere cannot leave stock negative.
            t.HasCheckConstraint("CK_Inventories_StockQuantity_NonNegative", "\"StockQuantity\" >= 0");
            t.HasCheckConstraint("CK_Inventories_LowStockThreshold_NonNegative", "\"LowStockThreshold\" >= 0");
        });

        builder.Ignore(i => i.IsLowStock);

        // One inventory row per product (a product belongs to exactly one shop).
        builder.HasIndex(i => i.ProductId).IsUnique();
        builder.HasIndex(i => new { i.ShopId, i.StockQuantity });

        builder.HasOne(i => i.Product)
            .WithOne(p => p.Inventory)
            .HasForeignKey<Inventory>(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Shop)
            .WithMany(s => s.Inventories)
            .HasForeignKey(i => i.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
