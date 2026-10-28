using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Application.Common;

/// <summary>
/// Bound from the "Store" configuration section. Currency and time zone apply to every shop;
/// per-shop values are on the roadmap.
/// </summary>
public sealed class StoreOptions
{
    public const string SectionName = "Store";

    /// <summary>Name given to the first shop created at bootstrap. After that the shop's own name (in the database) is used.</summary>
    [Required]
    public string Name { get; set; } = "SmartPOS Store";

    /// <summary>ISO 4217 code used by the UI to format money.</summary>
    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; set; } = "USD";

    /// <summary>
    /// IANA time-zone ID (e.g. "Asia/Kolkata"). Defines what "today" and a calendar day mean for
    /// the dashboard, date filters and daily reports. Timestamps are always stored in UTC.
    /// </summary>
    [Required]
    public string TimeZoneId { get; set; } = "UTC";
}

/// <summary>Bound from the "Pos" configuration section.</summary>
public sealed class PosOptions
{
    public const string SectionName = "Pos";

    [Range(0, 100)]
    public decimal TaxRatePercent { get; set; }

    /// <summary>Highest sale-level discount a bill may carry.</summary>
    [Range(0, 100)]
    public decimal MaxDiscountPercent { get; set; } = 100;
}
