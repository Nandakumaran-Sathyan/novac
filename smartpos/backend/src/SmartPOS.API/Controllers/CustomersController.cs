using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Common;
using SmartPOS.Application.Customers;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController(ICustomerService customerService) : ControllerBase
{
    /// <summary>Search and page through the shop's customers. Search matches name or phone.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CustomerDto>> List([FromQuery] CustomerQuery query, CancellationToken cancellationToken) =>
        customerService.ListAsync(query, cancellationToken);

    [HttpGet("{id:int}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CustomerDto> Get(int id, CancellationToken cancellationToken) =>
        customerService.GetAsync(id, cancellationToken);

    /// <summary>Register a customer. Cashiers can do this at the till.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CustomerDto> Update(int id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        customerService.UpdateAsync(id, request, cancellationToken);
}
