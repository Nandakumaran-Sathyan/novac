using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Inventory;

public sealed class InventoryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    StoreCalendar calendar) : IInventoryService
{
    internal static readonly Expression<Func<Product, InventoryItemDto>> ToItemDto = p => new InventoryItemDto(
        p.Id, p.Sku, p.Name, p.Category.Name, p.StockQuantity, p.ReorderLevel,
        p.StockQuantity <= 0 ? StockStatus.OutOfStock
            : p.StockQuantity <= p.ReorderLevel ? StockStatus.Low
            : StockStatus.InStock,
        p.IsActive, p.UpdatedAt);

    private static readonly Expression<Func<StockMovement, StockMovementDto>> ToMovementDto = m => new StockMovementDto(
        m.Id, m.ProductId, m.Product.Sku, m.Product.Name, m.QuantityChange, m.QuantityAfter, m.Type, m.Reason,
        m.SaleId, m.Sale != null ? m.Sale.InvoiceNumber : null, m.User != null ? m.User.FullName : null, m.CreatedAt);

    public async Task<PagedResult<InventoryItemDto>> ListAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();
        if (!query.IncludeInactive)
            products = products.Where(p => p.IsActive);
        if (query.CategoryId is { } categoryId)
            products = products.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            products = products.Where(p =>
                EF.Functions.Like(p.Name, pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(p.Sku, pattern, SearchPattern.EscapeCharacter));
        }
        products = query.Filter switch
        {
            InventoryFilter.Low => products.Where(p => p.StockQuantity <= p.ReorderLevel),
            InventoryFilter.OutOfStock => products.Where(p => p.StockQuantity <= 0),
            _ => products
        };

        return await products
            // Most urgent first: lowest stock relative to reorder level.
            .OrderBy(p => p.StockQuantity - p.ReorderLevel).ThenBy(p => p.Name)
            .Select(ToItemDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<StockMovementDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
                          .Where(p => p.Id == request.ProductId)
                          .Select(p => new { p.Id, p.StockQuantity })
                          .SingleOrDefaultAsync(cancellationToken)
                      ?? throw NotFoundException.For("Product", request.ProductId);

        StockRules.EnsureValidAdjustment(product.StockQuantity, request.QuantityChange);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var newQuantity = await db.TryChangeStockAsync(product.Id, request.QuantityChange, cancellationToken)
                          ?? throw new ConflictException("Stock changed while adjusting and the adjustment would now make it negative. Reload and try again.");

        var movement = new StockMovement
        {
            ProductId = product.Id,
            QuantityChange = request.QuantityChange,
            QuantityAfter = newQuantity,
            Type = StockMovementType.Adjustment,
            Reason = request.Reason.Trim(),
            UserId = currentUser.UserId,
            CreatedAt = calendar.UtcNow
        };
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await db.StockMovements.AsNoTracking()
            .Where(m => m.Id == movement.Id)
            .Select(ToMovementDto)
            .SingleAsync(cancellationToken);
    }

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(MovementQuery query, CancellationToken cancellationToken)
    {
        var movements = db.StockMovements.AsNoTracking();
        if (query.ProductId is { } productId)
            movements = movements.Where(m => m.ProductId == productId);
        if (query.Type is { } type)
            movements = movements.Where(m => m.Type == type);
        if (query.From is { } from)
        {
            var fromUtc = calendar.StartOfDayUtc(from);
            movements = movements.Where(m => m.CreatedAt >= fromUtc);
        }
        if (query.To is { } to)
        {
            var toUtc = calendar.StartOfDayUtc(to.AddDays(1));
            movements = movements.Where(m => m.CreatedAt < toUtc);
        }

        return await movements
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(ToMovementDto)
            .ToPagedResultAsync(query, cancellationToken);
    }
}
