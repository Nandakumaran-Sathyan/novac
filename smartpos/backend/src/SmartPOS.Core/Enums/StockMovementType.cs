namespace SmartPOS.Core.Enums;

public enum StockMovementType
{
    /// <summary>Opening stock recorded when a product is created.</summary>
    InitialStock = 1,
    /// <summary>Stock deducted by a completed sale.</summary>
    Sale = 2,
    /// <summary>Manual correction (restock, damage, count correction...). Always carries a reason.</summary>
    Adjustment = 3
}
