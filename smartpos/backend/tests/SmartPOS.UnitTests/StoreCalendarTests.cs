using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;

namespace SmartPOS.UnitTests;

public class StoreCalendarTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static StoreCalendar Kolkata(DateTimeOffset utcNow) =>
        new(Options.Create(new StoreOptions { TimeZoneId = "Asia/Kolkata" }), new FixedTime(utcNow));

    [Fact]
    public void Today_uses_store_time_zone()
    {
        // 20:00 UTC on 1 March is 01:30 on 2 March in India (UTC+05:30).
        var calendar = Kolkata(new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 3, 2), calendar.Today);
    }

    [Fact]
    public void Day_range_converts_store_midnights_to_utc()
    {
        var calendar = Kolkata(DateTimeOffset.UtcNow);
        var (from, to) = calendar.ToUtcRange(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2));

        Assert.Equal(new DateTime(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 3, 2, 18, 30, 0, DateTimeKind.Utc), to);
    }
}
