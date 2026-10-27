using SmartPOS.Application.Common;

namespace SmartPOS.Application.Inventory;

public interface IInventoryService
{
    Task<PagedResult<InventoryItemDto>> ListAsync(InventoryQuery query, CancellationToken cancellationToken);
    Task<StockMovementDto> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken);
    Task<PagedResult<StockMovementDto>> ListMovementsAsync(MovementQuery query, CancellationToken cancellationToken);
}
