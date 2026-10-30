using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
using SmartPOS.Application.Inventory;
using SmartPOS.Application.Reports;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.AI.Tools;

/// <summary>
/// The read-only questions the assistant can ask the database. Arguments are already validated by
/// <see cref="StoreToolRegistry"/>. None of these methods takes a shop: every query goes through
/// <see cref="IApplicationDbContext"/>, whose filter limits it to the signed-in user's shop, so the model
/// cannot reach another shop's data whatever it asks for.
/// </summary>
public interface IStoreAITools
{
    Task<SalesSummaryResult> GetSalesSummaryAsync(StorePeriod period, CancellationToken cancellationToken);
    Task<TopProductsResult> GetTopProductsAsync(StorePeriod period, int limit, TopProductSort rankBy, CancellationToken cancellationToken);
    Task<LowStockResult> GetLowStockProductsAsync(int limit, CancellationToken cancellationToken);
    Task<ExpiringProductsResult> GetExpiringProductsAsync(int withinDays, int limit, CancellationToken cancellationToken);
    Task<InventoryValueResult> GetInventoryValueAsync(CancellationToken cancellationToken);
    Task<UnsoldProductsResult> GetUnsoldProductsAsync(int days, int limit, CancellationToken cancellationToken);
}

public sealed class StoreAITools(
    IApplicationDbContext db,
    IReportService reports,
    IInventoryService inventory,
    StoreCalendar calendar,
    IOptions<StoreOptions> storeOptions) : IStoreAITools
{
    private string Currency => storeOptions.Value.CurrencyCode;

    /// <summary>Money to 2 decimals. PostgreSQL keeps 4 after price x quantity (12573.2500), which reads badly to a model.</summary>
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Reuses the Reports page's daily-sales query, so the assistant and the Reports page always agree.</summary>
    public async Task<SalesSummaryResult> GetSalesSummaryAsync(StorePeriod period, CancellationToken cancellationToken)
    {
        var (from, to) = StorePeriods.Resolve(period, calendar.Today);
        var report = await reports.GetDailySalesAsync(from, to, cancellationToken);
        return new SalesSummaryResult(StorePeriods.NameOf(period), from, to, Currency,
            Money(report.Total), report.TransactionCount, Money(report.AverageSale), Money(report.Days.Sum(d => d.TaxAmount)),
            "Completed bills only. TotalSales is what customers paid (after discounts, including tax).");
    }

    /// <summary>Reuses the Reports page's top-products query.</summary>
    public async Task<TopProductsResult> GetTopProductsAsync(StorePeriod period, int limit, TopProductSort rankBy, CancellationToken cancellationToken)
    {
        var (from, to) = StorePeriods.Resolve(period, calendar.Today);
        var rows = await reports.GetTopProductsAsync(from, to, limit, cancellationToken, rankBy);
        return new TopProductsResult(StorePeriods.NameOf(period), from, to, Currency, rankBy.ToString().ToLowerInvariant(),
            rows.Select(r => new ProductSalesRow(r.ProductName, r.Sku, r.QuantitySold, Money(r.Revenue), Money(r.GrossProfit))).ToList(),
            rows.Count == 0
                ? "No products were sold in this period."
                : "NetSales is after discounts and excludes tax. GrossProfit is NetSales minus the cost recorded at the time of each sale.");
    }

    /// <summary>Reuses the Inventory page's "Low" filter (at or below the low-stock level, most urgent first).</summary>
    public async Task<LowStockResult> GetLowStockProductsAsync(int limit, CancellationToken cancellationToken)
    {
        var page = await inventory.ListAsync(new InventoryQuery { Filter = InventoryFilter.Low, Page = 1, PageSize = limit }, cancellationToken);
        return new LowStockResult(page.TotalCount,
            page.Items.Select(i => new LowStockRow(i.Name, i.Sku, i.StockQuantity, i.LowStockThreshold,
                i.Status == StockStatus.OutOfStock ? "out of stock" : "low")).ToList(),
            page.TotalCount == 0
                ? "No active product is at or below its low-stock level."
                : $"Active products only, most urgent first. Showing {page.Items.Count} of {page.TotalCount}.");
    }

    public async Task<ExpiringProductsResult> GetExpiringProductsAsync(int withinDays, int limit, CancellationToken cancellationToken)
    {
        var today = calendar.Today;
        var until = today.AddDays(withinDays);
        var candidates = db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.ExpiryDate != null && p.ExpiryDate <= until);

        var expired = await candidates.CountAsync(p => p.ExpiryDate < today, cancellationToken);
        var soon = await candidates.CountAsync(p => p.ExpiryDate >= today, cancellationToken);
        var rows = await candidates
            .OrderBy(p => p.ExpiryDate).ThenBy(p => p.Name)
            .Take(limit)
            .Select(p => new { p.Name, p.Sku, ExpiryDate = p.ExpiryDate!.Value, InStock = p.Inventory != null ? p.Inventory.StockQuantity : 0 })
            .ToListAsync(cancellationToken);

        return new ExpiringProductsResult(today, withinDays, expired, soon,
            rows.Select(r => new ExpiringRow(r.Name, r.Sku, r.ExpiryDate, r.ExpiryDate.DayNumber - today.DayNumber, r.InStock)).ToList(),
            "Expiry dates are per product, not per batch. A negative DaysLeft means already expired. Products without an expiry date are not included.");
    }

    public async Task<InventoryValueResult> GetInventoryValueAsync(CancellationToken cancellationToken)
    {
        // Fetch the few columns needed and total in memory: one row per product, so it stays small, and it
        // avoids database differences in decimal aggregation.
        var stock = await db.Inventories.AsNoTracking()
            .Where(i => i.Product.IsActive && i.StockQuantity > 0)
            .Select(i => new { i.StockQuantity, i.Product.CostPrice, i.Product.Price })
            .ToListAsync(cancellationToken);

        var atCost = stock.Sum(s => s.StockQuantity * s.CostPrice);
        var atPrice = stock.Sum(s => s.StockQuantity * s.Price);
        return new InventoryValueResult(Currency, Money(atCost), Money(atPrice), Money(atPrice - atCost), stock.Count, stock.Sum(s => s.StockQuantity),
            "Active products with stock on hand, valued at today's cost price and selling price (not batch-by-batch costing). Selling-price value excludes tax.");
    }

    public async Task<UnsoldProductsResult> GetUnsoldProductsAsync(int days, int limit, CancellationToken cancellationToken)
    {
        var since = calendar.Today.AddDays(-(days - 1));
        var sinceUtc = calendar.StartOfDayUtc(since);

        // Products with no completed sale line on or after the cutoff.
        var unsold = db.Products.AsNoTracking()
            .Where(p => p.IsActive && !db.SaleItems.Any(i => i.ProductId == p.Id && i.Sale.Status == SaleStatus.Completed && i.Sale.CreatedAt >= sinceUtc));

        var total = await unsold.CountAsync(cancellationToken);
        var rows = await unsold
            .Select(p => new
            {
                p.Name, p.Sku, p.CostPrice,
                InStock = p.Inventory != null ? p.Inventory.StockQuantity : 0,
                LastSoldUtc = db.SaleItems.Where(i => i.ProductId == p.Id && i.Sale.Status == SaleStatus.Completed)
                    .Max(i => (DateTime?)i.Sale.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        // Most money tied up first. Ordered in memory: price x stock in SQL differs between databases.
        var products = rows
            .Select(r => new UnsoldRow(r.Name, r.Sku, r.InStock, Money(r.InStock * r.CostPrice),
                r.LastSoldUtc is { } last ? calendar.ToStoreDate(last) : null))
            .OrderByDescending(r => r.StockValueAtCost).ThenBy(r => r.Name)
            .Take(limit)
            .ToList();

        return new UnsoldProductsResult(days, since, Currency, total, products,
            total == 0
                ? $"Every active product has sold at least once in the last {days} days."
                : $"Active products with no completed sale in the last {days} days, most stock value first. LastSoldOn is empty if the product has never sold.");
    }
}
