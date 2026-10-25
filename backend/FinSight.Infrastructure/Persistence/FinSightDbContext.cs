using FinSight.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Infrastructure.Persistence;

public class FinSightDbContext(DbContextOptions<FinSightDbContext> options) : DbContext(options)
{
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<RuleAssessment> RuleAssessments => Set<RuleAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in this assembly (see Configurations/).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinSightDbContext).Assembly);
    }
}
