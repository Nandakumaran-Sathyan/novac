using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Application.Common;

/// <summary>Bound from the "Store" configuration section.</summary>
public sealed class StoreOptions
{
    public const string SectionName = "Store";

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

    /// <summary>Highest sale-level discount a cashier may apply.</summary>
    [Range(0, 100)]
    public decimal MaxCashierDiscountPercent { get; set; } = 10;

    /// <summary>Highest sale-level discount an admin may apply.</summary>
    [Range(0, 100)]
    public decimal MaxAdminDiscountPercent { get; set; } = 100;
}
