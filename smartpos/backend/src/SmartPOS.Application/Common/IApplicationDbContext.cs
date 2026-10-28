using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Common;

/// <summary>
/// The persistence surface the application services use. It exposes EF Core DbSets directly
/// (rather than a generic repository per entity) and adds one primitive that needs care to get
/// right under concurrency: <see cref="TryChangeStockAsync"/>.
/// Every shop-owned set is filtered to the current user's shop (see the DbContext's query filters).
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Shop> Shops { get; }
    DbSet<User> Users { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<SmartPOS.Core.Entities.Inventory> Inventories { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<StockMovement> StockMovements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically adds <paramref name="delta"/> to a product's inventory, but only if the result stays
    /// at or above zero. Runs as a single conditional UPDATE, so two concurrent sales can never both
    /// take the last unit. Returns the new quantity, or null if the product does not exist or the
    /// change would make stock negative.
    /// </summary>
    Task<int?> TryChangeStockAsync(int productId, int delta, CancellationToken cancellationToken = default);
}
