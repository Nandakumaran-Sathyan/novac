using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Application.Inventory;
using SmartPOS.Application.Sales;

namespace SmartPOS.Application.Dashboard;

public sealed record DashboardSummaryDto(
    DateOnly Date,
    decimal TodaySalesTotal,
    int TodayTransactionCount,
    int ProductCount,
    int LowStockCount,
    IReadOnlyList<SaleSummaryDto> RecentTransactions,
    IReadOnlyList<InventoryItemDto> LowStockProducts);

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
}

/// <summary>Every figure is a live query; an empty database yields zeros and empty lists.</summary>
public sealed class DashboardService(IApplicationDbContext db, StoreCalendar calendar) : IDashboardService
{
    private const int ListSize = 8;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var today = calendar.Today;
        var (fromUtc, toUtc) = calendar.ToUtcRange(today, today);

        var todaySales = db.Sales.AsNoTracking().Where(s => s.CreatedAt >= fromUtc && s.CreatedAt < toUtc);
        var todayTotal = await todaySales.SumAsync(s => s.Total, cancellationToken);
        var todayCount = await todaySales.CountAsync(cancellationToken);

        var activeProducts = db.Products.AsNoTracking().Where(p => p.IsActive);
        var productCount = await activeProducts.CountAsync(cancellationToken);
        var lowStock = activeProducts.Where(p => p.StockQuantity <= p.ReorderLevel);
        var lowStockCount = await lowStock.CountAsync(cancellationToken);

        var lowStockProducts = await lowStock
            .OrderBy(p => p.StockQuantity - p.ReorderLevel).ThenBy(p => p.Name)
            .Take(ListSize)
            .Select(InventoryService.ToItemDto)
            .ToListAsync(cancellationToken);

        var recent = await db.Sales.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(ListSize)
            .Select(SaleService.ToSummaryDto)
            .ToListAsync(cancellationToken);

        return new DashboardSummaryDto(today, todayTotal, todayCount, productCount, lowStockCount, recent, lowStockProducts);
    }
}
