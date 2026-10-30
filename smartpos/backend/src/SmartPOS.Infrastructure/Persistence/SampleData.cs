namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// Fictional catalogues and demo profiles for local development. A new shop gets the grocery catalogue
/// (Database:SeedSampleData); the reset-demo-data command can switch a shop to another profile. A few items
/// start below their low-stock threshold, and a few are near or past expiry, so those views have something to show.
/// </summary>
internal static class SampleData
{
    /// <param name="ShelfLifeDays">Days from now until the expiry date (negative = already expired); null for non-perishables.</param>
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

    /// <summary>
    /// A fictional pharmacy catalogue. Expiry dates are realistic for medicines (months to years), with a few
    /// close to or past expiry so the "nearing expiry" views and assistant tool have something to find.
    /// Generic names only; no real brands.
    /// </summary>
    public static readonly IReadOnlyList<ProductSeed> PharmacyProducts =
    [
        new("PHM-PCM-500", "Paracetamol 500 mg (10 tablets)", "Pain Relief", 25.00m, 15.00m, 220, 50, 540),
        new("PHM-IBU-400", "Ibuprofen 400 mg (10 tablets)", "Pain Relief", 32.00m, 19.00m, 140, 40, 480),
        new("PHM-DCL-GEL", "Diclofenac Gel 30 g", "Pain Relief", 95.00m, 62.00m, 35, 10, 400),
        new("PHM-CET-10", "Cetirizine 10 mg (10 tablets)", "Cold & Cough", 22.00m, 12.00m, 160, 40, 600),
        new("PHM-CSY-100", "Cough Syrup 100 ml", "Cold & Cough", 115.00m, 78.00m, 18, 20, 20),
        new("PHM-STM-CAP", "Steam Inhalation Capsules (10)", "Cold & Cough", 60.00m, 38.00m, 45, 15, 365),
        new("PHM-ORS-21", "ORS Sachet 21 g", "Digestive Care", 21.00m, 13.00m, 300, 60, 25),
        new("PHM-ANT-170", "Antacid Syrup 170 ml", "Digestive Care", 125.00m, 84.00m, 28, 12, 10),
        new("PHM-PRB-CAP", "Probiotic Capsules (10)", "Digestive Care", 140.00m, 95.00m, 6, 10, 180),
        new("PHM-VTC-500", "Vitamin C 500 mg (15 tablets)", "Vitamins & Supplements", 45.00m, 28.00m, 110, 30, 300),
        new("PHM-MLT-30", "Multivitamin (30 tablets)", "Vitamins & Supplements", 320.00m, 220.00m, 24, 8, 540),
        new("PHM-CAL-D3", "Calcium + Vitamin D3 (15 tablets)", "Vitamins & Supplements", 110.00m, 72.00m, 0, 10, 420),
        new("PHM-BND-100", "Adhesive Bandages (100)", "First Aid", 85.00m, 52.00m, 60, 15, 900),
        new("PHM-ANS-100", "Antiseptic Liquid 100 ml", "First Aid", 70.00m, 45.00m, 40, 12, 720),
        new("PHM-CTN-100", "Absorbent Cotton Roll 100 g", "First Aid", 55.00m, 33.00m, 3, 10),
        new("PHM-GLS-50", "Glucose Test Strips (50)", "Diabetes Care", 899.00m, 690.00m, 14, 5, 240),
        new("PHM-HSN-100", "Hand Sanitizer 100 ml", "Personal Care", 50.00m, 30.00m, 90, 25, 365),
        new("PHM-BBW-72", "Baby Wipes (72)", "Baby Care", 199.00m, 130.00m, 30, 10, -5)
    ];

    /// <summary>
    /// Everything that differs between kinds of shop when generating demo data: the catalogue, invented customers
    /// (phone numbers are obviously fake), which categories sell most, opening hours and busy times, bills per day,
    /// basket size and how often a known customer is attached. Weights are relative, not percentages.
    /// </summary>
    internal sealed record DemoProfile(
        string Name,
        IReadOnlyList<ProductSeed> Products,
        IReadOnlyList<(string Name, string Phone)> Customers,
        IReadOnlyDictionary<string, double> CategoryPopularity,
        double[] WeekdayFactor,          // Monday first
        int FirstHour,
        int[] HourWeight,                // one per opening hour, starting at FirstHour
        double BillsPerDay,
        int[] LinesPerBillWeight,        // weight of 1, 2, 3... lines
        int[] QuantityWeight,            // weight of quantity 1, 2, 3...
        double CustomerShare);

    /// <summary>The original demo: a neighbourhood grocery. Values unchanged, so existing shops' history is reproducible.</summary>
    public static readonly DemoProfile Grocery = new(
        "grocery",
        Products,
        [
            ("Aarav Mehta", "9000000101"), ("Diya Kapoor", "9000000102"), ("Kabir Singh", "9000000103"),
            ("Ananya Iyer", "9000000104"), ("Rohan Das", "9000000105"), ("Meera Nair", "9000000106"),
            ("Vihaan Reddy", "9000000107"), ("Ishita Bose", "9000000108"), ("Arjun Pillai", "9000000109"),
            ("Saanvi Joshi", "9000000110"), ("Reyansh Gupta", "9000000111"), ("Tara Menon", "9000000112"),
            ("Kian Shah", "9000000113"), ("Navya Rao", "9000000114"), ("Dev Malhotra", "9000000115"),
            ("Zoya Khan", "9000000116")
        ],
        new Dictionary<string, double>
        {
            ["Beverages"] = 1.6, ["Snacks"] = 1.4, ["Dairy"] = 1.3, ["Bakery"] = 1.0, ["Household"] = 0.5, ["Personal Care"] = 0.6
        },
        [0.9, 0.85, 0.95, 1.0, 1.15, 1.4, 1.25],
        8, [3, 5, 7, 9, 10, 8, 6, 5, 6, 9, 12, 12, 9, 4],
        26, [30, 28, 20, 12, 7, 3], [65, 25, 7, 2, 1], 0.38);

    /// <summary>
    /// A pharmacy: steadier through the week (slightly quieter on Sunday), morning and evening peaks, smaller
    /// baskets, and more regulars known by name (prescriptions, chronic care).
    /// </summary>
    public static readonly DemoProfile Pharmacy = new(
        "pharmacy",
        PharmacyProducts,
        [
            ("Lakshmi Narayanan", "9000000201"), ("Farhan Qureshi", "9000000202"), ("Priyanka Sharma", "9000000203"),
            ("Joseph Mathew", "9000000204"), ("Kavya Reddy", "9000000205"), ("Suresh Babu", "9000000206"),
            ("Ayesha Siddiqui", "9000000207"), ("Harish Kumar", "9000000208"), ("Neha Agarwal", "9000000209"),
            ("Rahul Verma", "9000000210"), ("Deepa Menon", "9000000211"), ("Vikram Chauhan", "9000000212")
        ],
        new Dictionary<string, double>
        {
            ["Pain Relief"] = 1.6, ["Cold & Cough"] = 1.4, ["Digestive Care"] = 1.1, ["Vitamins & Supplements"] = 0.9,
            ["First Aid"] = 0.7, ["Diabetes Care"] = 0.5, ["Personal Care"] = 0.8, ["Baby Care"] = 0.6
        },
        [1.0, 1.0, 1.0, 1.0, 1.05, 1.1, 0.85],
        8, [4, 9, 11, 9, 7, 6, 5, 5, 6, 8, 11, 12, 10, 6],
        20, [45, 30, 15, 7, 3], [60, 28, 8, 3, 1], 0.5);

    public static DemoProfile ProfileNamed(string name) => name.Trim().ToLowerInvariant() switch
    {
        "grocery" => Grocery,
        "pharmacy" => Pharmacy,
        _ => throw new ArgumentException($"Unknown demo profile '{name}'. Use grocery or pharmacy.")
    };
}
