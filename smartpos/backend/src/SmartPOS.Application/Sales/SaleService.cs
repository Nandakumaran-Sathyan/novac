using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
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
    ILogger<SaleService> logger) : ISaleService
{
    internal static readonly Expression<Func<Sale, SaleSummaryDto>> ToSummaryDto = s => new SaleSummaryDto(
        s.Id, s.InvoiceNumber, s.CreatedAt, s.Cashier.FullName, s.Items.Sum(i => i.Quantity), s.PaymentMethod, s.Total);

    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        var options = posOptions.Value;
        DiscountPolicy.EnsureAllowed(currentUser.Role, request.DiscountPercent, options);

        // Merge duplicate lines and sort by product ID. A consistent lock order means two
        // concurrent sales touching the same products cannot deadlock each other.
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .OrderBy(l => l.ProductId)
            .ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException("A sale must contain at least one item.");

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var (productId, quantity) in lines)
        {
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
                throw new BusinessRuleException($"Product {productId} does not exist or is not available for sale.");
            StockRules.EnsureAvailable(product.Name, product.StockQuantity, quantity);
        }

        // Prices come from the database, never from the client.
        var totals = SaleCalculator.Calculate(
            lines.Select(l => new SaleLineInput(products[l.ProductId].SellingPrice, l.Quantity)).ToList(),
            request.DiscountPercent,
            options.TaxRatePercent);

        var now = calendar.UtcNow;
        var sale = new Sale
        {
            InvoiceNumber = $"INV-{now:yyyyMMdd}-{RandomNumberGenerator.GetHexString(8)}",
            CashierId = currentUser.UserId,
            Subtotal = totals.Subtotal,
            DiscountPercent = request.DiscountPercent,
            DiscountAmount = totals.DiscountAmount,
            TaxRatePercent = options.TaxRatePercent,
            TaxAmount = totals.TaxAmount,
            Total = totals.Total,
            PaymentMethod = request.PaymentMethod,
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
                UnitPrice = product.SellingPrice,
                UnitCost = product.CostPrice,
                Quantity = quantity,
                LineTotal = totals.LineTotals[i]
            });
            db.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                Sale = sale,
                QuantityChange = -quantity,
                QuantityAfter = quantityAfter,
                Type = StockMovementType.Sale,
                Reason = $"Sale {sale.InvoiceNumber}",
                UserId = currentUser.UserId,
                CreatedAt = now
            });
        }

        db.Sales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Sale {InvoiceNumber} created by user {UserId}: {ItemCount} lines, total {Total}",
            sale.InvoiceNumber, currentUser.UserId, sale.Items.Count, sale.Total);
        return await GetAsync(sale.Id, cancellationToken);
    }

    public async Task<PagedResult<SaleSummaryDto>> ListAsync(SaleQuery query, CancellationToken cancellationToken)
    {
        var sales = VisibleSales();
        if (currentUser.IsAdmin && query.CashierId is { } cashierId)
            sales = sales.Where(s => s.CashierId == cashierId);
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
            sales = sales.Where(s => EF.Functions.Like(s.InvoiceNumber, pattern, SearchPattern.EscapeCharacter));
        }

        return await sales
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(ToSummaryDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<SaleDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        // A cashier asking for someone else's sale gets 404, not 403, so IDs can't be probed.
        var sale = await VisibleSales()
                       .Where(s => s.Id == id)
                       .Select(s => new SaleDto(
                           s.Id, s.InvoiceNumber, s.CreatedAt, s.CashierId, s.Cashier.FullName, s.PaymentMethod,
                           s.Subtotal, s.DiscountPercent, s.DiscountAmount, s.TaxRatePercent, s.TaxAmount, s.Total,
                           s.Items.OrderBy(i => i.Id)
                               .Select(i => new SaleItemDto(i.Id, i.ProductId, i.Sku, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal))
                               .ToList()))
                       .SingleOrDefaultAsync(cancellationToken)
                   ?? throw NotFoundException.For("Sale", id);
        return sale;
    }

    public PosSettingsDto GetPosSettings()
    {
        var store = storeOptions.Value;
        var pos = posOptions.Value;
        return new PosSettingsDto(store.Name, store.CurrencyCode, store.TimeZoneId, pos.TaxRatePercent,
            DiscountPolicy.MaxDiscountPercent(currentUser.Role, pos));
    }

    private IQueryable<Sale> VisibleSales()
    {
        var sales = db.Sales.AsNoTracking();
        return currentUser.IsAdmin ? sales : sales.Where(s => s.CashierId == currentUser.UserId);
    }
}
