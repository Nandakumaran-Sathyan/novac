using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Inventory;

public sealed record InventoryItemDto(
    int ProductId,
    string Sku,
    string Name,
    string CategoryName,
    int StockQuantity,
    int ReorderLevel,
    StockStatus Status,
    bool IsActive,
    DateTime UpdatedAt);

public sealed record StockMovementDto(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    int QuantityChange,
    int QuantityAfter,
    StockMovementType Type,
    string Reason,
    int? SaleId,
    string? InvoiceNumber,
    string? UserName,
    DateTime CreatedAt);

public enum InventoryFilter
{
    All,
    /// <summary>At or below reorder level, including out of stock.</summary>
    Low,
    OutOfStock
}

public sealed class InventoryQuery : PagedQuery
{
    [StringLength(100)]
    public string? Search { get; init; }

    public int? CategoryId { get; init; }

    public InventoryFilter Filter { get; init; } = InventoryFilter.All;

    public bool IncludeInactive { get; init; }
}

public sealed class StockAdjustmentRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    /// <summary>Positive to add stock (e.g. delivery), negative to remove it (e.g. damage).</summary>
    [Range(-100_000, 100_000)]
    public int QuantityChange { get; init; }

    [Required, StringLength(250, MinimumLength = 3)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class MovementQuery : PagedQuery
{
    public int? ProductId { get; init; }
    public StockMovementType? Type { get; init; }
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? From { get; init; }
    /// <summary>Store-local calendar date, inclusive.</summary>
    public DateOnly? To { get; init; }
}
