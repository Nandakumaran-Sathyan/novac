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
    internal static readonly Expression<Func<SmartPOS.Core.Entities.Inventory, InventoryItemDto>> ToItemDto = i => new InventoryItemDto(
        i.ProductId, i.Product.Sku, i.Product.Name, i.Product.Category, i.StockQuantity, i.LowStockThreshold,
        i.StockQuantity <= 0 ? StockStatus.OutOfStock
            : i.StockQuantity <= i.LowStockThreshold ? StockStatus.Low
            : StockStatus.InStock,
        i.Product.IsActive, i.LastUpdated);

    private static readonly Expression<Func<StockMovement, StockMovementDto>> ToMovementDto = m => new StockMovementDto(
        m.Id, m.ProductId, m.Product.Sku, m.Product.Name, m.QuantityChange, m.QuantityAfter, m.Type, m.Reason,
        m.SaleId, m.Sale != null ? m.Sale.BillNumber : null, m.User != null ? m.User.FullName : null, m.CreatedAt);

    public async Task<PagedResult<InventoryItemDto>> ListAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        var items = db.Inventories.AsNoTracking();
        if (!query.IncludeInactive)
            items = items.Where(i => i.Product.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            items = items.Where(i => i.Product.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            items = items.Where(i =>
                EF.Functions.Like(i.Product.Name.ToLower(), pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(i.Product.Sku.ToLower(), pattern, SearchPattern.EscapeCharacter));
        }
        items = query.Filter switch
        {
            InventoryFilter.Low => items.Where(i => i.StockQuantity <= i.LowStockThreshold),
            InventoryFilter.OutOfStock => items.Where(i => i.StockQuantity <= 0),
            _ => items
        };

        return await items
            // Most urgent first: lowest stock relative to the threshold.
            .OrderBy(i => i.StockQuantity - i.LowStockThreshold).ThenBy(i => i.Product.Name)
            .Select(ToItemDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<StockMovementDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Inventories.AsNoTracking()
                       .Where(i => i.ProductId == request.ProductId)
                       .Select(i => new { i.ProductId, i.StockQuantity })
                       .SingleOrDefaultAsync(cancellationToken)
                   ?? throw NotFoundException.For("Product", request.ProductId);

        StockRules.EnsureValidAdjustment(item.StockQuantity, request.QuantityChange);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        var newQuantity = await db.TryChangeStockAsync(item.ProductId, request.QuantityChange, cancellationToken)
                          ?? throw new ConflictException("Stock changed while adjusting and the adjustment would now make it negative. Reload and try again.");

        var movement = new StockMovement
        {
            ShopId = currentUser.ShopId,
            ProductId = item.ProductId,
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
