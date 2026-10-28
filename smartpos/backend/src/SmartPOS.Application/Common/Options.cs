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

/// <summary>Bound from the "Loyalty" configuration section.</summary>
public sealed class LoyaltyOptions
{
    public const string SectionName = "Loyalty";

    /// <summary>Amount a customer spends to earn one point (in the store currency). 100 means 1 point per 100 spent.</summary>
    [Range(typeof(decimal), "1", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal SpendPerPoint { get; set; } = 100;
}

/// <summary>Bound from the "App" configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public address of the web app, used to build links in emails (e.g. https://pos.example.com). Empty disables emailed links.</summary>
    public string WebBaseUrl { get; set; } = string.Empty;

    /// <summary>How long an email-verification link stays valid.</summary>
    [Range(1, 24 * 14)]
    public int EmailVerificationHours { get; set; } = 24;
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
