using Microsoft.Extensions.Options;

namespace SmartPOS.Application.Common;

/// <summary>
/// Converts between UTC timestamps (how everything is stored) and calendar dates in the store's
/// time zone (how people ask questions: "today", "sales on 3 March").
/// </summary>
public sealed class StoreCalendar(IOptions<StoreOptions> options, TimeProvider timeProvider)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly Today => ToStoreDate(UtcNow);

    public DateOnly ToStoreDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone));

    public DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

    /// <summary>UTC half-open range [from 00:00, day after to 00:00) covering whole store days.</summary>
    public (DateTime FromUtc, DateTime ToUtcExclusive) ToUtcRange(DateOnly from, DateOnly to) =>
        (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));
}
