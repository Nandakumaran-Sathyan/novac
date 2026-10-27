using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Common;
using SmartPOS.Application.Sales;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/sales")]
[Produces("application/json")]
public sealed class SalesController(ISaleService saleService) : ControllerBase
{
    /// <summary>
    /// Complete a sale. Prices come from the database; stock is checked and deducted in the same
    /// transaction that records the sale. Returns 409 if any item lacks stock.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SaleDto>> Create(CreateSaleRequest request, CancellationToken cancellationToken)
    {
        var sale = await saleService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = sale.Id }, sale);
    }

    /// <summary>Transaction history. Cashiers see only their own sales.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<SaleSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SaleSummaryDto>> List([FromQuery] SaleQuery query, CancellationToken cancellationToken) =>
        saleService.ListAsync(query, cancellationToken);

    /// <summary>A sale with its line items (the invoice).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SaleDto> Get(int id, CancellationToken cancellationToken) =>
        saleService.GetAsync(id, cancellationToken);
}
