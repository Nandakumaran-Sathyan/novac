using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Application.Inventory;
using SmartPOS.Application.Sales;
using SmartPOS.Core.Enums;

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

        var completed = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed);
        var todaySales = completed.Where(s => s.CreatedAt >= fromUtc && s.CreatedAt < toUtc);
        var todayTotal = await todaySales.SumAsync(s => s.TotalAmount, cancellationToken);
        var todayCount = await todaySales.CountAsync(cancellationToken);

        var productCount = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken);
        var lowStock = db.Inventories.AsNoTracking().Where(i => i.Product.IsActive && i.StockQuantity <= i.LowStockThreshold);
        var lowStockCount = await lowStock.CountAsync(cancellationToken);

        var lowStockProducts = await lowStock
            .OrderBy(i => i.StockQuantity - i.LowStockThreshold).ThenBy(i => i.Product.Name)
            .Take(ListSize)
            .Select(InventoryService.ToItemDto)
            .ToListAsync(cancellationToken);

        var recent = await completed
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(ListSize)
            .Select(SaleService.ToSummaryDto)
            .ToListAsync(cancellationToken);

        return new DashboardSummaryDto(today, todayTotal, todayCount, productCount, lowStockCount, recent, lowStockProducts);
    }
}
