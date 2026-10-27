using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems", t =>
        {
            t.HasCheckConstraint("CK_SaleItems_Quantity_Positive", "Quantity > 0");
            t.HasCheckConstraint("CK_SaleItems_UnitPrice_NonNegative", "UnitPrice >= 0");
        });

        builder.Property(i => i.ProductName).HasMaxLength(150).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(50).IsRequired();
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.UnitCost).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);

        // Restrict: a product with sales history can never be deleted, only deactivated.
        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
