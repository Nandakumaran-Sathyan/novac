using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales", t =>
        {
            t.HasCheckConstraint("CK_Sales_Total_NonNegative", "Total >= 0");
            t.HasCheckConstraint("CK_Sales_DiscountPercent_Range", "DiscountPercent >= 0 AND DiscountPercent <= 100");
        });

        builder.Property(s => s.InvoiceNumber).HasMaxLength(30).IsRequired();
        builder.Property(s => s.Subtotal).HasPrecision(18, 2);
        builder.Property(s => s.DiscountPercent).HasPrecision(5, 2);
        builder.Property(s => s.DiscountAmount).HasPrecision(18, 2);
        builder.Property(s => s.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(s => s.TaxAmount).HasPrecision(18, 2);
        builder.Property(s => s.Total).HasPrecision(18, 2);
        builder.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.HasIndex(s => s.CreatedAt);

        builder.HasOne(s => s.Cashier)
            .WithMany()
            .HasForeignKey(s => s.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.Sale)
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
