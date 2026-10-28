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
            t.HasCheckConstraint("CK_Sales_TotalAmount_NonNegative", "\"TotalAmount\" >= 0");
            t.HasCheckConstraint("CK_Sales_DiscountPercent_Range", "\"DiscountPercent\" >= 0 AND \"DiscountPercent\" <= 100");
            t.HasCheckConstraint("CK_Sales_ChangeAmount_NonNegative", "\"ChangeAmount\" >= 0");
            t.HasCheckConstraint("CK_Sales_AmountPaid_NonNegative", "\"AmountPaid\" >= 0");
        });

        builder.Property(s => s.BillNumber).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Subtotal).HasPrecision(12, 2);
        builder.Property(s => s.DiscountPercent).HasPrecision(5, 2);
        builder.Property(s => s.DiscountAmount).HasPrecision(12, 2);
        builder.Property(s => s.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(s => s.TaxAmount).HasPrecision(12, 2);
        builder.Property(s => s.TotalAmount).HasPrecision(12, 2);
        builder.Property(s => s.AmountPaid).HasPrecision(12, 2);
        builder.Property(s => s.ChangeAmount).HasPrecision(12, 2);
        builder.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(s => s.BillNumber).IsUnique();
        builder.HasIndex(s => new { s.ShopId, s.CreatedAt });
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.CustomerId);

        builder.HasOne(s => s.Shop)
            .WithMany(shop => shop.Sales)
            .HasForeignKey(s => s.ShopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A customer with bills can't be deleted out from under them.
        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.Sale)
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
