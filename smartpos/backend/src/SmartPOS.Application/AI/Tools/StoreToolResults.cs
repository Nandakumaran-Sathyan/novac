namespace SmartPOS.Application.AI.Tools;

// Results handed to the model. Deliberately compact (the model's context is small and it reads slowly) and
// self-describing: each carries its period and dates, its currency, and a short note on what the figures mean,
// so the model can state them accurately. Nothing here identifies a shop, a user or a customer.

public sealed record SalesSummaryResult(
    string Period, DateOnly From, DateOnly To, string Currency,
    decimal TotalSales, int Bills, decimal AverageBill, decimal TaxCollected, string Note);

public sealed record ProductSalesRow(string Name, string Sku, int UnitsSold, decimal NetSales, decimal GrossProfit);

public sealed record TopProductsResult(
    string Period, DateOnly From, DateOnly To, string Currency, string RankedBy,
    IReadOnlyList<ProductSalesRow> Products, string Note);

public sealed record LowStockRow(string Name, string Sku, int InStock, int LowStockLevel, string Status);

public sealed record LowStockResult(int TotalLowStockProducts, IReadOnlyList<LowStockRow> Products, string Note);

public sealed record ExpiringRow(string Name, string Sku, DateOnly ExpiryDate, int DaysLeft, int InStock);

public sealed record ExpiringProductsResult(
    DateOnly Today, int WithinDays, int AlreadyExpired, int ExpiringSoon, IReadOnlyList<ExpiringRow> Products, string Note);

public sealed record InventoryValueResult(
    string Currency, decimal ValueAtCost, decimal ValueAtSellingPrice, decimal PotentialGrossProfit,
    int ProductsInStock, int UnitsInStock, string Note);

public sealed record UnsoldRow(string Name, string Sku, int InStock, decimal StockValueAtCost, DateOnly? LastSoldOn);

public sealed record UnsoldProductsResult(
    int Days, DateOnly Since, string Currency, int TotalUnsoldProducts, IReadOnlyList<UnsoldRow> Products, string Note);
