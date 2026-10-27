using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            // Last line of defence: even a bug elsewhere cannot leave stock or prices negative.
            t.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "StockQuantity >= 0");
            t.HasCheckConstraint("CK_Products_ReorderLevel_NonNegative", "ReorderLevel >= 0");
            t.HasCheckConstraint("CK_Products_SellingPrice_NonNegative", "SellingPrice >= 0");
            t.HasCheckConstraint("CK_Products_CostPrice_NonNegative", "CostPrice >= 0");
        });

        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.Property(p => p.CostPrice).HasPrecision(18, 2);
        builder.Ignore(p => p.IsLowStock);

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Name);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
