using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Dashboard;

/// <summary>A figure for the chosen period next to the same figure for the period just before it.</summary>
public sealed record Kpi(decimal Current, decimal Previous);

public sealed record InsightKpis(
    Kpi Revenue,
    Kpi Bills,
    Kpi AverageBill,
    Kpi ItemsSold,
    Kpi Customers,
    Kpi GrossProfit);

// Revenue is the day's bill totals (tax included); PreviousRevenue is the matching day of the previous period.
public sealed record DailyPoint(DateOnly Date, decimal Revenue, int Bills, decimal PreviousRevenue);

public sealed record PaymentSlice(PaymentMethod Method, int Bills, decimal Amount);

/// <summary>Net sales (after discount, before tax) of one product category.</summary>
public sealed record CategorySlice(string Category, int Units, decimal Revenue);

public sealed record ProductPerformance(int ProductId, string Sku, string Name, int Units, decimal Revenue, decimal GrossProfit);

public sealed record CustomerPerformance(int CustomerId, string Name, int Bills, decimal Spent);

// Heatmap: bill counts for 7 weekdays (Monday first) × 24 hours, in store time.
public sealed record InsightsDto(
    DateOnly From,
    DateOnly To,
    int Days,
    InsightKpis Kpis,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<IReadOnlyList<int>> Heatmap,
    IReadOnlyList<PaymentSlice> Payments,
    IReadOnlyList<CategorySlice> Categories,
    IReadOnlyList<ProductPerformance> TopProducts,
    IReadOnlyList<CustomerPerformance> TopCustomers,
    int BillsWithCustomer,
    int BillsWalkIn);

public interface IInsightsService
{
    Task<InsightsDto> GetAsync(int days, CancellationToken cancellationToken);
}

/// <summary>
/// Sales analytics for the dashboard. Every figure covers Completed bills only, over the last
/// <c>days</c> store days (today included), and the KPIs are compared with the equally long period
/// before it. Days, weekdays and hours are in the store's time zone.
/// </summary>
public sealed class InsightsService(IApplicationDbContext db, StoreCalendar calendar) : IInsightsService
{
    public const int MaxDays = 180;

    public async Task<InsightsDto> GetAsync(int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxDays)
            throw new BusinessRuleException($"Days must be between 1 and {MaxDays}.");

        var to = calendar.Today;
        var from = to.AddDays(-(days - 1));
        var previousFrom = from.AddDays(-days);
        var (fromUtc, toUtc) = calendar.ToUtcRange(from, to);
        var (previousFromUtc, previousToUtc) = calendar.ToUtcRange(previousFrom, from.AddDays(-1));

        // One narrow query covers both periods; bucketing by store-local day/hour happens in memory
        // because the database does not know the store's time zone.
        var bills = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= previousFromUtc && s.CreatedAt < toUtc)
            .Select(s => new { s.Id, s.CreatedAt, s.TotalAmount, s.CustomerId, s.PaymentMethod })
            .ToListAsync(cancellationToken);

        var current = bills.Where(b => b.CreatedAt >= fromUtc).ToList();
        var previous = bills.Where(b => b.CreatedAt < previousToUtc).ToList();

        // Line items: net revenue (after discount, before tax), cost and units, per product.
        var itemRows = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.CreatedAt >= previousFromUtc && i.Sale.CreatedAt < toUtc)
            .Select(i => new
            {
                i.ProductId, i.ProductName, i.Sku, i.Quantity,
                Category = i.Product.Category,
                Net = i.LineTotal - i.TaxAmount,
                Cost = i.UnitCost * i.Quantity,
                Current = i.Sale.CreatedAt >= fromUtc
            })
            .ToListAsync(cancellationToken);
        var currentItems = itemRows.Where(i => i.Current).ToList();
        var previousItems = itemRows.Where(i => !i.Current).ToList();

        var currentRevenue = current.Sum(b => b.TotalAmount);
        var previousRevenue = previous.Sum(b => b.TotalAmount);
        var kpis = new InsightKpis(
            new Kpi(currentRevenue, previousRevenue),
            new Kpi(current.Count, previous.Count),
            new Kpi(Average(currentRevenue, current.Count), Average(previousRevenue, previous.Count)),
            new Kpi(currentItems.Sum(i => i.Quantity), previousItems.Sum(i => i.Quantity)),
            new Kpi(Distinct(current.Select(b => b.CustomerId)), Distinct(previous.Select(b => b.CustomerId))),
            new Kpi(currentItems.Sum(i => i.Net - i.Cost), previousItems.Sum(i => i.Net - i.Cost)));

        var currentByDay = current.ToLookup(b => calendar.ToStoreDate(b.CreatedAt));
        var previousByDay = previous.ToLookup(b => calendar.ToStoreDate(b.CreatedAt));
        var daily = Enumerable.Range(0, days).Select(offset =>
        {
            var day = from.AddDays(offset);
            var dayBills = currentByDay[day].ToList();
            return new DailyPoint(day, dayBills.Sum(b => b.TotalAmount), dayBills.Count,
                previousByDay[previousFrom.AddDays(offset)].Sum(b => b.TotalAmount));
        }).ToList();

        var heatmap = new int[7][];
        for (var d = 0; d < 7; d++) heatmap[d] = new int[24];
        foreach (var bill in current)
        {
            var local = calendar.ToStoreTime(bill.CreatedAt);
            heatmap[((int)local.DayOfWeek + 6) % 7][local.Hour]++; // Monday = 0
        }

        var payments = current.GroupBy(b => b.PaymentMethod)
            .Select(g => new PaymentSlice(g.Key, g.Count(), g.Sum(b => b.TotalAmount)))
            .OrderByDescending(p => p.Amount).ToList();

        var categories = currentItems.GroupBy(i => i.Category)
            .Select(g => new CategorySlice(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.Net)))
            .OrderByDescending(c => c.Revenue).ToList();

        var topProducts = currentItems.GroupBy(i => i.ProductId)
            .Select(g => new ProductPerformance(g.Key, g.First().Sku, g.First().ProductName,
                g.Sum(i => i.Quantity), g.Sum(i => i.Net), g.Sum(i => i.Net - i.Cost)))
            .OrderByDescending(p => p.Revenue).Take(6).ToList();

        var customerTotals = current.Where(b => b.CustomerId != null)
            .GroupBy(b => b.CustomerId!.Value)
            .Select(g => new { Id = g.Key, Bills = g.Count(), Spent = g.Sum(b => b.TotalAmount) })
            .OrderByDescending(c => c.Spent).Take(5).ToList();
        var ids = customerTotals.Select(c => c.Id).ToList();
        var names = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var topCustomers = customerTotals
            .Select(c => new CustomerPerformance(c.Id, names.GetValueOrDefault(c.Id, "Customer"), c.Bills, c.Spent)).ToList();

        var withCustomer = current.Count(b => b.CustomerId != null);
        return new InsightsDto(from, to, days, kpis, daily, heatmap, payments, categories, topProducts, topCustomers,
            withCustomer, current.Count - withCustomer);
    }

    private static decimal Average(decimal total, int count) =>
        count == 0 ? 0 : Math.Round(total / count, 2, MidpointRounding.AwayFromZero);

    private static int Distinct(IEnumerable<int?> customerIds) => customerIds.Where(id => id != null).Distinct().Count();
}
