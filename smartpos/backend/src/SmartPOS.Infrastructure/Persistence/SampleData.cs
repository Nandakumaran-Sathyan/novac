namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// Fictional catalogue for local development (enabled by Database:SeedSampleData), added to the first
/// shop. It contains no sales and no users, so the dashboard starts at zero until real transactions
/// are made. A few items start below their low-stock threshold so the low-stock views have something to show.
/// </summary>
internal static class SampleData
{
    /// <param name="ShelfLifeDays">Days from now until the expiry date; null for non-perishables.</param>
    internal sealed record ProductSeed(
        string Sku, string Name, string Category, decimal Price, decimal CostPrice, int Stock, int LowStockThreshold,
        int? ShelfLifeDays = null);

    public static readonly IReadOnlyList<ProductSeed> Products =
    [
        new("BEV-COLA-500", "Cola 500 ml", "Beverages", 40.00m, 28.00m, 120, 24),
        new("BEV-ORJ-1L", "Orange Juice 1 L", "Beverages", 110.00m, 82.00m, 18, 20),
        new("BEV-TEA-250", "Black Tea 250 g", "Beverages", 145.00m, 105.00m, 45, 10),
        new("BEV-WTR-1L", "Mineral Water 1 L", "Beverages", 20.00m, 12.00m, 200, 48),
        new("SNK-CHP-SLT", "Salted Potato Chips 90 g", "Snacks", 30.00m, 20.00m, 80, 20),
        new("SNK-BSC-CRM", "Cream Biscuits 150 g", "Snacks", 35.00m, 24.50m, 6, 15),
        new("SNK-CHC-BAR", "Milk Chocolate Bar 50 g", "Snacks", 60.00m, 42.00m, 64, 12),
        new("DRY-MLK-1L", "Toned Milk 1 L", "Dairy", 56.00m, 48.00m, 30, 25, 5),
        new("DRY-CRD-400", "Curd 400 g", "Dairy", 45.00m, 34.00m, 0, 10, 7),
        new("DRY-BTR-100", "Salted Butter 100 g", "Dairy", 58.00m, 47.00m, 22, 8, 60),
        new("BAK-BRD-WHT", "White Bread 400 g", "Bakery", 45.00m, 33.00m, 25, 10, 4),
        new("BAK-BUN-4PK", "Burger Buns (4 pack)", "Bakery", 40.00m, 28.00m, 4, 8, 3),
        new("HSH-DSH-500", "Dishwash Liquid 500 ml", "Household", 99.00m, 70.00m, 35, 6),
        new("HSH-FLR-1L", "Floor Cleaner 1 L", "Household", 189.00m, 140.00m, 14, 5),
        new("PRC-SOP-100", "Bath Soap 100 g", "Personal Care", 38.00m, 26.00m, 90, 20),
        new("PRC-SHM-180", "Shampoo 180 ml", "Personal Care", 165.00m, 118.00m, 12, 6)
    ];
}
