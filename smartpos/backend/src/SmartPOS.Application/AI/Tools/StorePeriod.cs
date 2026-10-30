namespace SmartPOS.Application.AI.Tools;

/// <summary>
/// The only time ranges the model may ask for. The model picks a name; the server works out the dates in the
/// store's time zone, so the model never supplies raw dates (no "sales for 1990", no unbounded ranges).
/// </summary>
public enum StorePeriod
{
    Today,
    Yesterday,
    Last7Days,
    ThisMonth,
    LastMonth,
    Last30Days
}

public static class StorePeriods
{
    /// <summary>The names the model uses, exactly as they appear in the tool schemas.</summary>
    public static readonly IReadOnlyDictionary<string, StorePeriod> ByName = new Dictionary<string, StorePeriod>(StringComparer.OrdinalIgnoreCase)
    {
        ["today"] = StorePeriod.Today,
        ["yesterday"] = StorePeriod.Yesterday,
        ["last7days"] = StorePeriod.Last7Days,
        ["thisMonth"] = StorePeriod.ThisMonth,
        ["lastMonth"] = StorePeriod.LastMonth,
        ["last30days"] = StorePeriod.Last30Days
    };

    public static string NameOf(StorePeriod period) => ByName.First(p => p.Value == period).Key;

    /// <summary>Inclusive calendar dates for a period, relative to the store's today.</summary>
    public static (DateOnly From, DateOnly To) Resolve(StorePeriod period, DateOnly today) => period switch
    {
        StorePeriod.Today => (today, today),
        StorePeriod.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
        StorePeriod.Last7Days => (today.AddDays(-6), today),
        StorePeriod.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
        StorePeriod.LastMonth => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
        StorePeriod.Last30Days => (today.AddDays(-29), today),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };
}
