using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Reports;

public sealed record DailySalesRow(
    DateOnly Date,
    int TransactionCount,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total);

public sealed record DailySalesReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailySalesRow> Days,
    int TransactionCount,
    decimal Total,
    decimal AverageSale);

/// <summary>Revenue is net sales: line totals after discount and excluding tax. Profit is revenue minus cost.</summary>
public sealed record TopProductRow(
    int ProductId,
    string Sku,
    string ProductName,
    int QuantitySold,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit);

public interface IReportService
{
    Task<DailySalesReport> GetDailySalesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
    Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateOnly? from, DateOnly? to, int limit, CancellationToken cancellationToken);
}

public sealed class ReportService(IApplicationDbContext db, StoreCalendar calendar) : IReportService
{
    public const int MaxRangeDays = 366;
    public const int DefaultRangeDays = 7;

    public async Task<DailySalesReport> GetDailySalesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (start, end) = ResolveRange(from, to);
        var (fromUtc, toUtc) = calendar.ToUtcRange(start, end);

        // Fetch the few columns needed and bucket by store-local date in memory. Grouping in SQL
        // would need the database to know the store's time zone, which MySQL may not have loaded.
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= fromUtc && s.CreatedAt < toUtc)
            .Select(s => new { s.CreatedAt, s.Subtotal, s.DiscountAmount, s.TaxAmount, s.TotalAmount })
            .ToListAsync(cancellationToken);

        var byDate = sales.ToLookup(s => calendar.ToStoreDate(s.CreatedAt));
        var days = new List<DailySalesRow>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var d = byDate[date].ToList();
            days.Add(new DailySalesRow(date, d.Count, d.Sum(s => s.Subtotal), d.Sum(s => s.DiscountAmount),
                d.Sum(s => s.TaxAmount), d.Sum(s => s.TotalAmount)));
        }

        var total = sales.Sum(s => s.TotalAmount);
        var average = sales.Count == 0 ? 0 : Math.Round(total / sales.Count, 2, MidpointRounding.AwayFromZero);
        return new DailySalesReport(start, end, days, sales.Count, total, average);
    }

    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateOnly? from, DateOnly? to, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 50)
            throw new BusinessRuleException("Limit must be between 1 and 50.");
        var (start, end) = ResolveRange(from, to);
        var (fromUtc, toUtc) = calendar.ToUtcRange(start, end);

        var rows = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.CreatedAt >= fromUtc && i.Sale.CreatedAt < toUtc)
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Sku = g.Max(i => i.Sku),
                ProductName = g.Max(i => i.ProductName),
                QuantitySold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal - i.TaxAmount),
                Cost = g.Sum(i => i.UnitCost * i.Quantity)
            })
            .ToListAsync(cancellationToken);

        // Ordered in memory: one row per product sold in the range, so the set stays small.
        return rows
            .OrderByDescending(r => r.Revenue).ThenByDescending(r => r.QuantitySold)
            .Take(limit)
            .Select(r => new TopProductRow(r.ProductId, r.Sku ?? string.Empty, r.ProductName ?? string.Empty, r.QuantitySold, r.Revenue, r.Cost, r.Revenue - r.Cost))
            .ToList();
    }

    private (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var end = to ?? calendar.Today;
        var start = from ?? end.AddDays(-(DefaultRangeDays - 1));
        if (start > end)
            throw new BusinessRuleException("'from' must be on or before 'to'.");
        if (end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
            throw new BusinessRuleException($"Date range cannot exceed {MaxRangeDays} days.");
        return (start, end);
    }
}
