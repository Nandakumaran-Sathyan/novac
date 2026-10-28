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
            t.HasCheckConstraint("CK_Products_Price_NonNegative", "\"Price\" >= 0");
            t.HasCheckConstraint("CK_Products_CostPrice_NonNegative", "\"CostPrice\" >= 0");
        });

        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Barcode).HasMaxLength(50);
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.CostPrice).HasPrecision(18, 2);

        // SKU and barcode are unique per shop (two shops may both stock "COLA-500"). PostgreSQL allows
        // many NULLs in a unique index, so products without a barcode don't collide.
        builder.HasIndex(p => new { p.ShopId, p.Sku }).IsUnique();
        builder.HasIndex(p => new { p.ShopId, p.Barcode }).IsUnique();
        builder.HasIndex(p => new { p.ShopId, p.Category });
        builder.HasIndex(p => p.Name);

        builder.HasOne(p => p.Shop)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
