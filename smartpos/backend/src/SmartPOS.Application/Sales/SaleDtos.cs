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
}

public sealed record SaleItemDto(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record SaleDto(
    int Id,
    string InvoiceNumber,
    DateTime CreatedAt,
    int CashierId,
    string CashierName,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal Total,
    IReadOnlyList<SaleItemDto> Items);

public sealed record SaleSummaryDto(
    int Id,
    string InvoiceNumber,
    DateTime CreatedAt,
    string CashierName,
    int ItemCount,
    PaymentMethod PaymentMethod,
    decimal Total);

public sealed class SaleQuery : PagedQuery
{
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? From { get; init; }
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Matches part of the invoice number.</summary>
    [StringLength(50)]
    public string? Search { get; init; }

    /// <summary>Admins only; cashiers always see just their own sales.</summary>
    public int? CashierId { get; init; }
}

/// <summary>Configuration the POS screen needs to preview totals and enforce limits.</summary>
public sealed record PosSettingsDto(
    string StoreName,
    string CurrencyCode,
    string TimeZoneId,
    decimal TaxRatePercent,
    decimal MaxDiscountPercent);
