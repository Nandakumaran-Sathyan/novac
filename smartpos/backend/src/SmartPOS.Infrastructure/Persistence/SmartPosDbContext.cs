using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence;

public sealed class SmartPosDbContext(DbContextOptions<SmartPosDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartPosDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // MySQL DATETIME has no time-zone information, so values come back as DateTimeKind.Unspecified.
        // All timestamps are written in UTC; this marks them as UTC on the way back so they serialise
        // with a trailing "Z" and the browser converts them to local time correctly.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task<int?> TryChangeStockAsync(int productId, int delta, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // UPDATE Products SET StockQuantity = StockQuantity + @delta ... WHERE Id = @id AND StockQuantity >= -@delta
        // The check and the write are one statement, and InnoDB holds the row lock until the
        // surrounding transaction ends, so concurrent sales are serialised per product.
        var affected = await Products
            .Where(p => p.Id == productId && p.StockQuantity >= -delta)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity + delta)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);

        if (affected == 0)
            return null;

        return await Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync(cancellationToken);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
