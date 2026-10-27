using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPOS.API.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Application.Inventory;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController(IInventoryService inventoryService) : ControllerBase
{
    /// <summary>Current stock levels, most urgent first. Filter=Low returns items at or below reorder level.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<InventoryItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<InventoryItemDto>> List([FromQuery] InventoryQuery query, CancellationToken cancellationToken) =>
        inventoryService.ListAsync(query, cancellationToken);

    /// <summary>Record a manual stock change with a reason. Cannot make stock negative.</summary>
    [HttpPost("adjustments")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockMovementDto>> Adjust(StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var movement = await inventoryService.AdjustAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, movement);
    }

    /// <summary>Stock movement history, newest first.</summary>
    [HttpGet("movements")]
    [ProducesResponseType<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<StockMovementDto>> Movements([FromQuery] MovementQuery query, CancellationToken cancellationToken) =>
        inventoryService.ListMovementsAsync(query, cancellationToken);
}
