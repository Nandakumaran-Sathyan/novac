using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Auth;

namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet run --project src/SmartPOS.API -- reset-demo-data --email owner@example.com --profile pharmacy --confirm</c>
/// <para>
/// DESTRUCTIVE, development only. Deletes ONE shop's products, stock, stock movements, customers and sales (every
/// row with that shop's ShopId, and the sale lines of those sales) and seeds the chosen demo profile in their
/// place, with fabricated history. The shop and its owner account are kept, so the owner signs in as before.
/// Other shops are untouched: every delete is filtered by this shop's id. All-or-nothing (one transaction).
/// </para>
/// </summary>
public sealed class ResetDemoDataCommand(SmartPosDbContext db, SampleDataSeeder seeder)
{
    public sealed record Outcome(int ShopId, string ShopName, int Products, int Customers, int Sales, int Movements, string Profile);

    public async Task<(Outcome Deleted, Outcome Created)> RunAsync(string email, string profileName, int days, CancellationToken cancellationToken)
    {
        var profile = SampleData.ProfileNamed(profileName);
        var normalized = AuthService.NormalizeEmail(email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                   ?? throw new InvalidOperationException($"No account with the email '{normalized}'.");
        var shopId = user.ShopId;
        var shop = await db.Shops.SingleAsync(s => s.Id == shopId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Every delete names this shop explicitly (the request-time shop filter is not active in a command).
        // Order matters: movements point at sales and products, sale lines at sales and products.
        var movements = await db.StockMovements.IgnoreQueryFilters().Where(m => m.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        await db.SaleItems.IgnoreQueryFilters().Where(i => i.Sale.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        var sales = await db.Sales.IgnoreQueryFilters().Where(s => s.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        var customers = await db.Customers.IgnoreQueryFilters().Where(c => c.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        await db.Inventories.IgnoreQueryFilters().Where(i => i.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        var products = await db.Products.IgnoreQueryFilters().Where(p => p.ShopId == shopId).ExecuteDeleteAsync(cancellationToken);
        var deleted = new Outcome(shopId, shop.ShopName, products, customers, sales, movements, "previous");

        await seeder.SeedProfileAsync(shopId, profile, withHistory: true, historyDays: days, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var created = new Outcome(shopId, shop.ShopName,
            await db.Products.IgnoreQueryFilters().CountAsync(p => p.ShopId == shopId, cancellationToken),
            await db.Customers.IgnoreQueryFilters().CountAsync(c => c.ShopId == shopId, cancellationToken),
            await db.Sales.IgnoreQueryFilters().CountAsync(s => s.ShopId == shopId, cancellationToken),
            await db.StockMovements.IgnoreQueryFilters().CountAsync(m => m.ShopId == shopId, cancellationToken),
            profile.Name);
        return (deleted, created);
    }
}
