using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", t =>
            t.HasCheckConstraint("CK_StockMovements_QuantityAfter_NonNegative", "QuantityAfter >= 0"));

        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Reason).HasMaxLength(250).IsRequired();

        builder.HasIndex(m => new { m.ProductId, m.CreatedAt });
        builder.HasIndex(m => m.CreatedAt);

        // Cascade only matters for products with no sales (others cannot be deleted).
        builder.HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Sale)
            .WithMany()
            .HasForeignKey(m => m.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
