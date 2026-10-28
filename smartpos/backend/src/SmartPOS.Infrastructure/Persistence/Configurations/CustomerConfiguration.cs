using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", t =>
            t.HasCheckConstraint("CK_Customers_LoyaltyPoints_NonNegative", "\"LoyaltyPoints\" >= 0"));

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20).IsRequired();

        builder.HasIndex(c => new { c.ShopId, c.Phone }).IsUnique();

        builder.HasOne(c => c.Shop)
            .WithMany(s => s.Customers)
            .HasForeignKey(c => c.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
