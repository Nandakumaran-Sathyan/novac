using System.Globalization;
using SmartPOS.Application.Common;
using SmartPOS.Application.Customers;
using SmartPOS.Application.Sales;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// FABRICATED sales history for demos and screenshots. Everything here is invented: fictional customers
/// with obviously fake phone numbers, bills drawn from a seeded random generator (so a given shop gets the
/// same history every time) shaped by plausible patterns: weekends busier, lunch and evening peaks, slow growth,
/// a few popular products, mostly cash and UPI. It runs only when Database:SeedDemoHistory is on, which
/// only appsettings.Development.json does.
/// Bills go through the real <see cref="SaleCalculator"/>, and stock is simulated so the ledger is
/// consistent: each product starts with its target stock plus everything that will be sold, then
/// the sales run down to exactly the target.
/// </summary>
internal static class DemoHistory
{
    public sealed record Result(
        IReadOnlyList<Customer> Customers,
        IReadOnlyList<Sale> Sales,
        IReadOnlyList<StockMovement> Movements,
        DateTime StartUtc,
        IReadOnlyDictionary<Product, int> OpeningStock);

    /// <param name="targetStock">Stock each product should have once the history has played out.</param>
    public static Result Generate(
        int shopId, int userId, IReadOnlyDictionary<Product, int> targetStock,
        StoreCalendar calendar, DateTime nowUtc, decimal taxRatePercent, decimal maxDiscountPercent, int days,
        decimal loyaltySpendPerPoint = 100, IReadOnlySet<string>? existingPhones = null, SampleData.DemoProfile? profile = null)
    {
        profile ??= SampleData.Grocery;
        // Seeded per shop: reproducible for a given shop, but two shops never produce the same bill numbers
        // (BillNumber is unique across the whole database).
        var rng = new Random(20261028 + shopId);
        var products = targetStock.Keys.ToList();
        var productWeights = products.Select((p, i) =>
            (profile.CategoryPopularity.GetValueOrDefault(p.Category, 1.0) * (0.6 + 0.8 * new Random(i * 7919 + 13).NextDouble()))).ToArray();

        var customers = profile.Customers.Where(c => existingPhones is null || !existingPhones.Contains(c.Phone)).Select(c => new Customer
        {
            ShopId = shopId, Name = c.Name, Phone = c.Phone, CreatedAt = nowUtc.AddDays(-days)
        }).ToList();
        var customerWeights = customers.Select((_, i) => 1.0 / Math.Pow(i + 1, 0.8)).ToArray();
        var useCustomers = customers.Count > 0;

        var today = calendar.ToStoreDate(nowUtc);
        var firstDay = today.AddDays(-(days - 1));
        var billNumbers = new HashSet<string>();
        var sales = new List<Sale>();
        var sold = products.ToDictionary(p => p, _ => 0);

        for (var d = 0; d < days; d++)
        {
            var date = firstDay.AddDays(d);
            var growth = 1.0 + 0.3 * d / Math.Max(days - 1, 1);
            var weekday = ((int)date.DayOfWeek + 6) % 7;
            var spike = d == days * 4 / 9 ? 1.6 : 1.0; // one festival-style rush
            var billCount = (int)Math.Round(profile.BillsPerDay * growth * profile.WeekdayFactor[weekday] * spike * (0.85 + 0.3 * rng.NextDouble()));

            for (var b = 0; b < billCount; b++)
            {
                var hour = profile.FirstHour + Pick(rng, profile.HourWeight.Select(w => (double)w).ToArray());
                var createdAt = calendar.StartOfDayUtc(date).AddHours(hour).AddMinutes(rng.Next(60)).AddSeconds(rng.Next(60));
                if (createdAt > nowUtc)
                    continue; // today only has the bills up to now

                var lineCount = 1 + Pick(rng, profile.LinesPerBillWeight);
                var chosen = new List<(Product Product, int Quantity)>();
                var weights = (double[])productWeights.Clone();
                for (var l = 0; l < Math.Min(lineCount, products.Count); l++)
                {
                    var index = Pick(rng, weights);
                    weights[index] = 0; // each product at most once per bill
                    var quantity = 1 + Pick(rng, profile.QuantityWeight);
                    chosen.Add((products[index], quantity));
                }

                var discount = Math.Min(maxDiscountPercent, new[] { 0m, 5m, 10m, 15m }[Pick(rng, [82, 9, 7, 2])]);
                var method = new[] { PaymentMethod.Cash, PaymentMethod.UPI, PaymentMethod.Card, PaymentMethod.Other }[Pick(rng, [40, 38, 18, 4])];
                var totals = SaleCalculator.Calculate(chosen.Select(c => new SaleLineInput(c.Product.Price, c.Quantity)).ToList(), discount, taxRatePercent);
                var paid = totals.Total;
                if (method == PaymentMethod.Cash && rng.NextDouble() < 0.5)
                {
                    var note = new[] { 10m, 50m, 100m, 500m }[Pick(rng, [20, 30, 35, 15])];
                    paid = Math.Ceiling(totals.Total / note) * note;
                }

                var customer = useCustomers && rng.NextDouble() < profile.CustomerShare ? customers[Pick(rng, customerWeights)] : null;
                string bill;
                do { bill = $"BILL-{date:yyyyMMdd}-{rng.Next():X8}"; } while (!billNumbers.Add(bill));

                var sale = new Sale
                {
                    BillNumber = bill, ShopId = shopId, UserId = userId, Customer = customer,
                    Subtotal = totals.Subtotal, DiscountPercent = discount, DiscountAmount = totals.DiscountAmount,
                    TaxRatePercent = taxRatePercent, TaxAmount = totals.TaxAmount, TotalAmount = totals.Total,
                    PaymentMethod = method, AmountPaid = paid, ChangeAmount = paid - totals.Total,
                    LoyaltyPointsEarned = customer is null ? 0 : LoyaltyRules.PointsEarned(totals.Total, loyaltySpendPerPoint),
                    Status = SaleStatus.Completed, CreatedAt = createdAt
                };
                if (customer is not null)
                    customer.LoyaltyPoints += sale.LoyaltyPointsEarned; // the balance is exactly what the bills earned
                for (var i = 0; i < chosen.Count; i++)
                {
                    var (product, quantity) = chosen[i];
                    sale.Items.Add(new SaleItem
                    {
                        Product = product, ProductName = product.Name, Sku = product.Sku, Quantity = quantity,
                        UnitPrice = product.Price, UnitCost = product.CostPrice, DiscountAmount = totals.LineDiscounts[i],
                        TaxAmount = totals.LineTaxes[i], LineTotal = totals.LineTotals[i]
                    });
                    sold[product] += quantity;
                }
                sales.Add(sale);
            }
        }

        // Replay the sales in time order to build a consistent stock ledger.
        var startUtc = calendar.StartOfDayUtc(firstDay);
        var opening = products.ToDictionary(p => p, p => targetStock[p] + sold[p]);
        var running = new Dictionary<Product, int>(opening);
        var movements = new List<StockMovement>();
        foreach (var sale in sales.OrderBy(s => s.CreatedAt))
        {
            foreach (var item in sale.Items)
            {
                running[item.Product] -= item.Quantity;
                movements.Add(new StockMovement
                {
                    ShopId = shopId, Product = item.Product, Sale = sale, QuantityChange = -item.Quantity,
                    QuantityAfter = running[item.Product], Type = StockMovementType.Sale,
                    Reason = $"Sale {sale.BillNumber}", UserId = userId, CreatedAt = sale.CreatedAt
                });
            }
        }

        return new Result(customers, sales, movements, startUtc, opening);
    }

    /// <summary>Picks an index with probability proportional to its weight.</summary>
    private static int Pick(Random rng, IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        var roll = rng.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0 && weights[i] > 0)
                return i;
        }
        return weights.Count - 1;
    }

    private static int Pick(Random rng, int[] weights) => Pick(rng, weights.Select(w => (double)w).ToArray());
}
