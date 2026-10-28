using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Persistence;

public sealed class SmartPosDbContext(DbContextOptions<SmartPosDbContext> options, ICurrentShop? currentShop = null)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Read by the query filters below on every query (EF re-evaluates it per context instance).
    // Null means "no shop": the filters then match no rows, so forgetting to set a shop fails closed.
    private int? CurrentShopId => currentShop?.ShopId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmartPosDbContext).Assembly);

        // Multi-shop isolation: every query on a shop-owned table is restricted to the signed-in
        // user's shop. Code that legitimately spans shops (login, token validation, startup
        // seeding) must opt out explicitly with IgnoreQueryFilters().
        modelBuilder.Entity<User>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        modelBuilder.Entity<Product>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        modelBuilder.Entity<Inventory>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        modelBuilder.Entity<Sale>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        modelBuilder.Entity<StockMovement>().HasQueryFilter(e => e.ShopId == CurrentShopId);
        // Sale lines have no ShopId of their own; they follow their sale.
        modelBuilder.Entity<SaleItem>().HasQueryFilter(e => e.Sale.ShopId == CurrentShopId);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Npgsql only accepts DateTimeKind.Utc for `timestamp with time zone` columns, so every timestamp
        // is normalised to UTC on the way in; on the way out it stays marked UTC, so values serialise
        // with a trailing "Z" and the browser converts them to local time correctly.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task<int?> TryChangeStockAsync(int productId, int delta, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // UPDATE Inventories SET StockQuantity = StockQuantity + @delta ... WHERE ProductId = @id AND StockQuantity >= -@delta
        // The check and the write are one statement, and InnoDB holds the row lock until the
        // surrounding transaction ends, so concurrent sales are serialised per product. The shop
        // query filter applies here too, so another shop's stock can never be touched.
        var affected = await Inventories
            .Where(i => i.ProductId == productId && i.StockQuantity >= -delta)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.StockQuantity, i => i.StockQuantity + delta)
                .SetProperty(i => i.LastUpdated, now), cancellationToken);

        if (affected == 0)
            return null;

        return await Inventories.Where(i => i.ProductId == productId).Select(i => i.StockQuantity).SingleAsync(cancellationToken);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
