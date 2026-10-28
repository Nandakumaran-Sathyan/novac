using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.EmailVerificationTokenHash).HasMaxLength(64);
        builder.HasIndex(u => u.Email).IsUnique();
        // One user per shop: the shop's owner.
        builder.HasIndex(u => u.ShopId).IsUnique();

        builder.HasOne(u => u.Shop)
            .WithMany(s => s.Users)
            .HasForeignKey(u => u.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
