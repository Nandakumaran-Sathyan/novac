using SmartPOS.Application.Common;

namespace SmartPOS.Application.Inventory;

public enum StockStatus
{
    InStock,
    Low,
    OutOfStock
}

/// <summary>
/// Stock checks that give the user a clear message. They run before the database update; the
/// conditional UPDATE in <see cref="IApplicationDbContext.TryChangeStockAsync"/> remains the
/// authoritative guard against races.
/// </summary>
public static class StockRules
{
    public static void EnsureAvailable(string productName, int onHand, int requested)
    {
        if (requested <= 0)
            throw new BusinessRuleException("Item quantities must be greater than zero.");
        if (requested > onHand)
            throw new ConflictException(
                $"Insufficient stock for '{productName}': requested {requested}, available {Math.Max(onHand, 0)}.");
    }

    public static void EnsureValidAdjustment(int onHand, int quantityChange)
    {
        if (quantityChange == 0)
            throw new BusinessRuleException("Quantity change cannot be zero.");
        if (onHand + quantityChange < 0)
            throw new ConflictException(
                $"Adjustment would make stock negative: {onHand} on hand, change of {quantityChange}.");
    }

    /// <summary>"Low" means at or below the reorder level, matching the dashboard's low-stock count.</summary>
    public static StockStatus GetStatus(int onHand, int reorderLevel) =>
        onHand <= 0 ? StockStatus.OutOfStock
        : onHand <= reorderLevel ? StockStatus.Low
        : StockStatus.InStock;
}
