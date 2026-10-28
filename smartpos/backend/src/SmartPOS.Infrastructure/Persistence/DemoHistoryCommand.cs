using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// `dotnet run --project src/SmartPOS.API -- seed-demo-history --email you@example.com [--days 90]`
/// Adds FABRICATED sales history (see <see cref="DemoHistory"/>) to a shop that already exists, without
/// touching its real records. The history is placed entirely before the shop's first real activity, and each
/// product's opening-stock movement is moved back to the start of the history, so the stock ledger stays
/// consistent from the first day through to the real sales. Development only; refuses to run twice.
/// </summary>
public sealed class DemoHistoryCommand(
    SmartPosDbContext db,
    StoreCalendar calendar,
    IOptions<PosOptions> posOptions,
    TimeProvider timeProvider)
{
    public async Task<string> RunAsync(string email, int days, CancellationToken cancellationToken)
    {
        var normalized = AuthService.NormalizeEmail(email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                   ?? throw new InvalidOperationException($"No account with the email '{normalized}'.");
        var shopId = user.ShopId;

        var products = await db.Products.IgnoreQueryFilters().Include(p => p.Inventory)
            .Where(p => p.ShopId == shopId).ToListAsync(cancellationToken);
        if (products.Count == 0)
            throw new InvalidOperationException("The shop has no products. Register it with sample data, or add products first.");

        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, cancellationToken);
        var movements = await db.StockMovements.IgnoreQueryFilters().Where(m => m.ShopId == shopId).ToListAsync(cancellationToken);

        // A shop that has been given history has bills and stock movements dated before the shop itself existed.
        var created = shop.CreatedAt.AddMinutes(-1);
        if (movements.Any(m => m.CreatedAt < created) ||
            await db.Sales.IgnoreQueryFilters().AnyAsync(s => s.ShopId == shopId && s.CreatedAt < created, cancellationToken))
            throw new InvalidOperationException("This shop already has history dated before it was created; nothing was added.");

        // Everything fabricated happens before the shop's first real activity.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = movements.Count > 0 ? movements.Min(m => m.CreatedAt) : now;

        // Stock as it stood at the cutoff = today's stock minus everything that happened after the opening entry.
        var target = products.ToDictionary(
            p => p,
            p => Math.Max(0, (p.Inventory?.StockQuantity ?? 0) -
                             movements.Where(m => m.ProductId == p.Id && m.Type != StockMovementType.InitialStock).Sum(m => m.QuantityChange)));

        var existingPhones = (await db.Customers.IgnoreQueryFilters().Where(c => c.ShopId == shopId).Select(c => c.Phone)
            .ToListAsync(cancellationToken)).ToHashSet();
        var history = DemoHistory.Generate(shopId, user.Id, target, calendar, cutoff, posOptions.Value.TaxRatePercent,
            posOptions.Value.MaxDiscountPercent, Math.Clamp(days, 1, 365), existingPhones);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var product in products)
        {
            var opening = history.OpeningStock[product];
            var initial = movements.Where(m => m.ProductId == product.Id && m.Type == StockMovementType.InitialStock)
                .OrderBy(m => m.CreatedAt).FirstOrDefault();
            if (initial is not null)
            {
                initial.CreatedAt = history.StartUtc;
                initial.QuantityChange = opening;
                initial.QuantityAfter = opening;
            }
            else if (opening > 0)
            {
                db.StockMovements.Add(new StockMovement
                {
                    ShopId = shopId, Product = product, QuantityChange = opening, QuantityAfter = opening,
                    Type = StockMovementType.InitialStock, Reason = "Sample data opening stock", CreatedAt = history.StartUtc
                });
            }
        }
        db.Customers.AddRange(history.Customers);
        db.Sales.AddRange(history.Sales);
        db.StockMovements.AddRange(history.Movements);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return $"Added {history.Sales.Count:N0} fabricated bills, {history.Customers.Count} customers and {history.Movements.Count:N0} stock movements " +
               $"to shop {shopId} ({days} days up to {calendar.ToStoreDate(cutoff):yyyy-MM-dd}). Your real records were not changed.";
    }
}
