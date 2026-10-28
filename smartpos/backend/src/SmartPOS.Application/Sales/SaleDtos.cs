using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Sales;

public sealed class SaleItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, 10_000)]
    public int Quantity { get; init; }
}

/// <summary>
/// Note what is NOT here: prices and totals. The client sends only what was bought; the server
/// prices it from the database so a tampered request cannot change what the customer pays.
/// </summary>
public sealed class CreateSaleRequest
{
    [Required, MinLength(1, ErrorMessage = "A sale must contain at least one item."), MaxLength(200)]
    public List<SaleItemRequest> Items { get; init; } = new();

    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal DiscountPercent { get; init; }

    [EnumDataType(typeof(PaymentMethod))]
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;

    /// <summary>
    /// Amount received. Optional: omitted means "exactly the total". Cash may exceed the total (the
    /// difference is returned as change); other methods must match it.
    /// </summary>
    [Range(typeof(decimal), "0", "9999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal? AmountPaid { get; init; }

    /// <summary>Optional customer for the bill; must belong to the same shop.</summary>
    [Range(1, int.MaxValue)]
    public int? CustomerId { get; init; }
}

public sealed record SaleItemDto(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record SaleDto(
    int Id,
    string BillNumber,
    DateTime CreatedAt,
    int UserId,
    string CashierName,
    int? CustomerId,
    string? CustomerName,
    SaleStatus Status,
    PaymentMethod PaymentMethod,
    int LoyaltyPointsEarned,
    int? CustomerLoyaltyPoints,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal ChangeAmount,
    IReadOnlyList<SaleItemDto> Items);

public sealed record SaleSummaryDto(
    int Id,
    string BillNumber,
    DateTime CreatedAt,
    string CashierName,
    string? CustomerName,
    int ItemCount,
    SaleStatus Status,
    PaymentMethod PaymentMethod,
    decimal TotalAmount);

public sealed class SaleQuery : PagedQuery
{
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? From { get; init; }
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Matches part of the bill number.</summary>
    [StringLength(50)]
    public string? Search { get; init; }
}

/// <summary>Configuration the POS screen needs to preview totals and enforce limits.</summary>
public sealed record PosSettingsDto(
    string StoreName,
    string StoreAddress,
    string CurrencyCode,
    string TimeZoneId,
    decimal TaxRatePercent,
    decimal MaxDiscountPercent,
    decimal LoyaltySpendPerPoint);
