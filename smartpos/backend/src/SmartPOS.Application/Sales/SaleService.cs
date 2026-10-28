using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
using SmartPOS.Application.Customers;
using SmartPOS.Application.Inventory;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Sales;

public sealed class SaleService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    StoreCalendar calendar,
    IOptions<PosOptions> posOptions,
    IOptions<StoreOptions> storeOptions,
    IOptions<LoyaltyOptions> loyaltyOptions,
    ILogger<SaleService> logger) : ISaleService
{
    internal static readonly Expression<Func<Sale, SaleSummaryDto>> ToSummaryDto = s => new SaleSummaryDto(
        s.Id, s.BillNumber, s.CreatedAt, s.User.FullName, s.Customer != null ? s.Customer.Name : null,
        s.Items.Sum(i => i.Quantity), s.Status, s.PaymentMethod, s.TotalAmount);

    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        var options = posOptions.Value;
        DiscountPolicy.EnsureAllowed(request.DiscountPercent, options);

        // Merge duplicate lines and sort by product ID. A consistent lock order means two
        // concurrent sales touching the same products cannot deadlock each other.
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .OrderBy(l => l.ProductId)
            .ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException("A sale must contain at least one item.");

        if (request.CustomerId is { } customerId &&
            !await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
            throw new BusinessRuleException($"Customer {customerId} does not exist.");

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Sku, p.Price, p.CostPrice, p.IsActive, Stock = p.Inventory != null ? p.Inventory.StockQuantity : 0 })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var (productId, quantity) in lines)
        {
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
                throw new BusinessRuleException($"Product {productId} does not exist or is not available for sale.");
            StockRules.EnsureAvailable(product.Name, product.Stock, quantity);
        }

        // Prices come from the database, never from the client.
        var totals = SaleCalculator.Calculate(
            lines.Select(l => new SaleLineInput(products[l.ProductId].Price, l.Quantity)).ToList(),
            request.DiscountPercent,
            options.TaxRatePercent);
        var (amountPaid, changeAmount) = SaleCalculator.Settle(totals.Total, request.PaymentMethod, request.AmountPaid);

        var now = calendar.UtcNow;
        var sale = new Sale
        {
            BillNumber = $"BILL-{now:yyyyMMdd}-{RandomNumberGenerator.GetHexString(8)}",
            ShopId = currentUser.ShopId,
            UserId = currentUser.UserId,
            CustomerId = request.CustomerId,
            Subtotal = totals.Subtotal,
            DiscountPercent = request.DiscountPercent,
            DiscountAmount = totals.DiscountAmount,
            TaxRatePercent = options.TaxRatePercent,
            TaxAmount = totals.TaxAmount,
            TotalAmount = totals.Total,
            PaymentMethod = request.PaymentMethod,
            AmountPaid = amountPaid,
            ChangeAmount = changeAmount,
            LoyaltyPointsEarned = request.CustomerId is null ? 0 : LoyaltyRules.PointsEarned(totals.Total, loyaltyOptions.Value.SpendPerPoint),
            Status = SaleStatus.Completed,
            CreatedAt = now
        };

        // Everything below commits together or not at all. If any stock update fails, disposing
        // the transaction without committing rolls back the stock already deducted for earlier lines.
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        for (var i = 0; i < lines.Count; i++)
        {
            var (productId, quantity) = lines[i];
            var product = products[productId];

            var quantityAfter = await db.TryChangeStockAsync(productId, -quantity, cancellationToken)
                                ?? throw new ConflictException($"Insufficient stock for '{product.Name}'. It may have just been sold; refresh and try again.");

            sale.Items.Add(new SaleItem
            {
                ProductId = productId,
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = quantity,
                UnitPrice = product.Price,
                UnitCost = product.CostPrice,
                DiscountAmount = totals.LineDiscounts[i],
                TaxAmount = totals.LineTaxes[i],
                LineTotal = totals.LineTotals[i]
            });
            db.StockMovements.Add(new StockMovement
            {
                ShopId = currentUser.ShopId,
                ProductId = productId,
                Sale = sale,
                QuantityChange = -quantity,
                QuantityAfter = quantityAfter,
                Type = StockMovementType.Sale,
                Reason = $"Sale {sale.BillNumber}",
                UserId = currentUser.UserId,
                CreatedAt = now
            });
        }

        db.Sales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);
        if (sale.LoyaltyPointsEarned > 0)
        {
            // Add to the balance in the database (not read-modify-write) so two bills for one customer can't lose points.
            var points = sale.LoyaltyPointsEarned;
            await db.Customers.Where(c => c.Id == request.CustomerId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LoyaltyPoints, c => c.LoyaltyPoints + points), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Sale {BillNumber} created by user {UserId}: {ItemCount} lines, total {Total}",
            sale.BillNumber, currentUser.UserId, sale.Items.Count, sale.TotalAmount);
        return await GetAsync(sale.Id, cancellationToken);
    }

    public async Task<PagedResult<SaleSummaryDto>> ListAsync(SaleQuery query, CancellationToken cancellationToken)
    {
        var sales = VisibleSales();
        if (query.From is { } from)
        {
            var fromUtc = calendar.StartOfDayUtc(from);
            sales = sales.Where(s => s.CreatedAt >= fromUtc);
        }
        if (query.To is { } to)
        {
            var toUtc = calendar.StartOfDayUtc(to.AddDays(1));
            sales = sales.Where(s => s.CreatedAt < toUtc);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            sales = sales.Where(s => EF.Functions.Like(s.BillNumber.ToLower(), pattern, SearchPattern.EscapeCharacter));
        }

        return await sales
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(ToSummaryDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<SaleDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        // A sale from another shop is not visible at all, so it gets 404, not 403: IDs can't be probed.
        var sale = await VisibleSales()
                       .Where(s => s.Id == id)
                       .Select(s => new SaleDto(
                           s.Id, s.BillNumber, s.CreatedAt, s.UserId, s.User.FullName,
                           s.CustomerId, s.Customer != null ? s.Customer.Name : null,
                           s.Status, s.PaymentMethod, s.LoyaltyPointsEarned, s.Customer != null ? s.Customer.LoyaltyPoints : null,
                           s.Subtotal, s.DiscountPercent, s.DiscountAmount, s.TaxRatePercent, s.TaxAmount,
                           s.TotalAmount, s.AmountPaid, s.ChangeAmount,
                           s.Items.OrderBy(i => i.Id)
                               .Select(i => new SaleItemDto(i.Id, i.ProductId, i.Sku, i.ProductName, i.Quantity,
                                   i.UnitPrice, i.DiscountAmount, i.TaxAmount, i.LineTotal))
                               .ToList()))
                       .SingleOrDefaultAsync(cancellationToken)
                   ?? throw NotFoundException.For("Sale", id);
        return sale;
    }

    public async Task<PosSettingsDto> GetPosSettingsAsync(CancellationToken cancellationToken)
    {
        var store = storeOptions.Value;
        var pos = posOptions.Value;
        var shop = await db.Shops.AsNoTracking()
            .Where(s => s.Id == currentUser.ShopId)
            .Select(s => new { s.ShopName, s.Address })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Shop", currentUser.ShopId);
        return new PosSettingsDto(shop.ShopName, shop.Address, store.CurrencyCode, store.TimeZoneId, pos.TaxRatePercent,
            DiscountPolicy.MaxDiscountPercent(pos), loyaltyOptions.Value.SpendPerPoint);
    }

    /// <summary>The shop's own sales (the DbContext's shop filter does the scoping).</summary>
    private IQueryable<Sale> VisibleSales() => db.Sales.AsNoTracking();
}
