using FinSight.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Infrastructure.Persistence;

/// <summary>
/// Entirely synthetic demo data: names are invented and no figure relates to a real person.
/// Each record is shaped to exercise a specific rule path (see the trailing comments).
///
/// Wired up through EF Core's UseSeeding/UseAsyncSeeding, which run after
/// `dotnet ef database update` or Database.Migrate(). Seeding is idempotent: it does
/// nothing when any application already exists.
/// </summary>
public static class SeedData
{
    public static void Seed(DbContext context)
    {
        var set = context.Set<LoanApplication>();
        if (set.Any())
            return;

        set.AddRange(CreateApplications(DateTime.UtcNow));
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        var set = context.Set<LoanApplication>();
        if (await set.AnyAsync(cancellationToken))
            return;

        set.AddRange(CreateApplications(DateTime.UtcNow));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<LoanApplication> CreateApplications(DateTime now)
    {
        var day = 0;
        LoanApplication App(string name, decimal? income, decimal? debt, decimal amount, decimal rate, int tenure,
            EmploymentType? employment, LoanStatus status = LoanStatus.Submitted) => new()
        {
            ApplicantName = name,
            MonthlyIncome = income,
            ExistingMonthlyDebt = debt,
            LoanAmount = amount,
            AnnualInterestRate = rate,
            TenureMonths = tenure,
            EmploymentType = employment,
            Status = status,
            CreatedAt = now.AddDays(-(day++)).AddHours(-3)
        };

        return
        [
            App("Ananya Rao",          95_000m,   8_000m,   600_000m, 10.75m, 48, EmploymentType.Salaried),      // clean
            App("Vikram Sethi",        62_000m,  15_000m,   450_000m, 11.50m, 36, EmploymentType.Salaried),      // DTI near limit
            App("Meera Kulkarni",     140_000m,       0m, 1_500_000m, 10.25m, 60, EmploymentType.SelfEmployed),  // clean, no existing debt
            App("Rohan Das",           48_000m,  18_000m,   800_000m, 13.00m, 36, EmploymentType.Salaried),      // DTI above max
            App("Priya Nair",              null,  5_000m,   300_000m, 12.00m, 24, EmploymentType.Salaried),      // missing income
            App("Arjun Bhatt",         75_000m,      null,   500_000m, 11.00m, 48, EmploymentType.SelfEmployed),  // missing existing debt
            App("Kavya Reddy",        210_000m,  25_000m, 3_500_000m, 10.50m, 72, EmploymentType.SelfEmployed),  // loan above product max
            App("Sameer Joshi",        55_000m,   4_000m,   400_000m, 12.50m, 120, EmploymentType.Salaried),     // tenure above product max
            App("Divya Pillai",        18_000m,   2_000m,   100_000m, 14.00m, 24, EmploymentType.Salaried),      // income below min
            App("Farhan Qureshi",      40_000m,  42_000m,   200_000m, 13.50m, 36, EmploymentType.Salaried),      // debt exceeds income
            App("Neha Chawla",         30_000m,       0m,   150_000m, 12.00m, 24, EmploymentType.Unemployed),    // unemployed with income
            App("Karthik Subramanian", 88_000m,  12_000m,   700_000m, 26.00m, 48, null),                         // rate out of range + missing employment
            App("Ishita Ghosh",       120_000m,  20_000m,    30_000m, 11.00m, 12, EmploymentType.Salaried),      // loan below product min
            App("Rahul Verma",         70_000m,  10_000m,   550_000m,  0.00m, 36, EmploymentType.Salaried),      // 0% promo rate: zero-interest EMI path
            App("Sneha Kapoor",        65_000m,   9_000m,   350_000m, 10.99m, 36, EmploymentType.Retired, LoanStatus.UnderReview),
            App("Aditya Malhotra",    150_000m,  30_000m, 1_200_000m,  9.99m, 60, EmploymentType.Salaried, LoanStatus.Approved),
            App("Pooja Iyer",          52_000m,  20_000m,   600_000m, 14.50m, 60, EmploymentType.SelfEmployed, LoanStatus.Rejected),
        ];
    }
}
