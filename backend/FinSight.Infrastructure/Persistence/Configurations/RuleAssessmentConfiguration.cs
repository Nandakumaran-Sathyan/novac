using FinSight.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSight.Infrastructure.Persistence.Configurations;

public class RuleAssessmentConfiguration : IEntityTypeConfiguration<RuleAssessment>
{
    public void Configure(EntityTypeBuilder<RuleAssessment> builder)
    {
        builder.ToTable("RuleAssessments", t =>
            t.HasCheckConstraint("CK_RuleAssessments_FlagsJson_IsJson", "ISJSON([FlagsJson]) = 1"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Emi).HasPrecision(18, 2);

        // A ratio, not money: 4 decimal places (0.4321 = 43.21%), with headroom for
        // pathological inputs such as a very small declared income.
        builder.Property(r => r.DebtToIncomeRatio).HasPrecision(18, 4);

        builder.Property(r => r.FlagsJson).IsRequired();
        builder.Property(r => r.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Restrict rather than cascade: deleting an application must not silently
        // erase its assessment history.
        builder.HasOne(r => r.Application)
            .WithMany(a => a.Assessments)
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves "latest assessment for application X" and doubles as the FK index.
        builder.HasIndex(r => new { r.ApplicationId, r.CreatedAt });
    }
}
