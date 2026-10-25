using FinSight.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSight.Infrastructure.Persistence.Configurations;

public class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        // Check constraints are the last line of defence: they hold even if a future
        // code path (or a manual SQL script) bypasses API validation.
        builder.ToTable("LoanApplications", t =>
        {
            t.HasCheckConstraint("CK_LoanApplications_LoanAmount_Positive", "[LoanAmount] > 0");
            t.HasCheckConstraint("CK_LoanApplications_TenureMonths_Range", "[TenureMonths] BETWEEN 1 AND 600");
            t.HasCheckConstraint("CK_LoanApplications_AnnualInterestRate_Range", "[AnnualInterestRate] BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_LoanApplications_MonthlyIncome_NonNegative", "[MonthlyIncome] IS NULL OR [MonthlyIncome] >= 0");
            t.HasCheckConstraint("CK_LoanApplications_ExistingMonthlyDebt_NonNegative", "[ExistingMonthlyDebt] IS NULL OR [ExistingMonthlyDebt] >= 0");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ApplicantName).IsRequired().HasMaxLength(100);

        // decimal(18,2): exact money to the paisa; up to 9,999,999,999,999,999.99.
        builder.Property(a => a.MonthlyIncome).HasPrecision(18, 2);
        builder.Property(a => a.ExistingMonthlyDebt).HasPrecision(18, 2);
        builder.Property(a => a.LoanAmount).HasPrecision(18, 2);

        // decimal(5,2): percentages such as 10.50 up to 100.00.
        builder.Property(a => a.AnnualInterestRate).HasPrecision(5, 2);

        // Enums stored as readable strings ("Salaried") rather than ints, so the data stays
        // meaningful in SQL queries and reports, and reordering the enum can't corrupt it.
        builder.Property(a => a.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // Supports the dashboard's main query: filter by status, newest first.
        builder.HasIndex(a => new { a.Status, a.CreatedAt });
    }
}
