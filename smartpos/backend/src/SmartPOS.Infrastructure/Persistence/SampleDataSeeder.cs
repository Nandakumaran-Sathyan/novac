using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Give every newly registered shop a sample catalogue. Development only.</summary>
    public bool SeedSampleData { get; set; }

    /// <summary>
    /// Also fabricate sales history (bills, customers and the matching stock movements) for the new shop,
    /// so the dashboards have something to show. Development only; requires <see cref="SeedSampleData"/>.
    /// </summary>
    public bool SeedDemoHistory { get; set; }

    /// <summary>How many days of fabricated history to generate, ending today.</summary>
    public int DemoHistoryDays { get; set; } = 90;
}

/// <summary>
/// Adds fictional products (with stock) to a newly registered shop so the screens have something to
/// show. By default it creates no users, customers or sales (Database:SeedDemoHistory adds fabricated ones,
/// see <see cref="DemoHistory"/>), and it runs only when Database:SeedSampleData is on.
/// The schema itself is never created here: that is done explicitly with `dotnet ef database update`.
/// </summary>
public sealed class SampleDataSeeder(
    SmartPosDbContext db,
    IOptions<DatabaseOptions> options,
    IOptions<PosOptions> posOptions,
    IOptions<LoyaltyOptions> loyaltyOptions,
    StoreCalendar calendar,
    TimeProvider timeProvider) : ISampleDataSeeder
{
    public async Task SeedAsync(int shopId, CancellationToken cancellationToken)
    {
        if (!options.Value.SeedSampleData)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<(Product Product, SampleData.ProductSeed Seed)>();
        foreach (var p in SampleData.Products)
        {
            var product = new Product
            {
                ShopId = shopId,
                Sku = p.Sku,
                Name = p.Name,
                Category = p.Category,
                Price = p.Price,
                CostPrice = p.CostPrice,
                ExpiryDate = p.ShelfLifeDays is { } days ? DateOnly.FromDateTime(now).AddDays(days) : null,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Inventory = new Inventory
                {
                    ShopId = shopId,
                    StockQuantity = p.Stock,
                    LowStockThreshold = p.LowStockThreshold,
                    LastUpdated = now
                }
            };
            db.Products.Add(product);
            created.Add((product, p));
        }

        DemoHistory.Result? history = null;
        if (options.Value.SeedDemoHistory)
        {
            var userId = await db.Users.IgnoreQueryFilters().Where(u => u.ShopId == shopId)
                .Select(u => u.Id).FirstAsync(cancellationToken);
            history = DemoHistory.Generate(
                shopId, userId, created.ToDictionary(c => c.Product, c => c.Seed.Stock), calendar, now,
                posOptions.Value.TaxRatePercent, posOptions.Value.MaxDiscountPercent, Math.Clamp(options.Value.DemoHistoryDays, 1, 365),
                loyaltyOptions.Value.SpendPerPoint);
            db.Customers.AddRange(history.Customers);
            db.Sales.AddRange(history.Sales);
            db.StockMovements.AddRange(history.Movements);
        }

        // Opening stock: with fabricated history it is what the shop "started" with, dated at the first day;
        // without, it is simply the sample stock dated now.
        foreach (var (product, seed) in created)
        {
            var opening = history?.OpeningStock[product] ?? seed.Stock;
            if (history is not null)
                product.Inventory!.StockQuantity = seed.Stock; // the sales run it down to the target
            if (opening > 0)
            {
                db.StockMovements.Add(new StockMovement
                {
                    ShopId = shopId,
                    Product = product,
                    QuantityChange = opening,
                    QuantityAfter = opening,
                    Type = StockMovementType.InitialStock,
                    Reason = "Sample data opening stock",
                    CreatedAt = history?.StartUtc ?? now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
