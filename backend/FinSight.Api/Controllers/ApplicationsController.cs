using FinSight.Application.DTOs;
using FinSight.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace FinSight.Api.Controllers;

/// <summary>
/// Thin HTTP adapter: binds input, calls a service, maps the result to a status code.
/// [ApiController] automatically returns 400 ValidationProblemDetails when DTO validation fails,
/// so no action needs to check ModelState itself.
/// </summary>
[ApiController]
[Route("api/applications")]
[Produces("application/json")]
public class ApplicationsController(LoanApplicationService applications, AssessmentService assessments) : ControllerBase
{
    /// <summary>List applications, newest first, with optional name search and status filter.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<LoanApplicationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<LoanApplicationSummaryDto>>> List(
        [FromQuery] LoanApplicationQuery query, CancellationToken ct) =>
        Ok(await applications.ListAsync(query, ct));

    /// <summary>Get one application with its most recent rule assessment (if any).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<LoanApplicationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoanApplicationDetailDto>> Get(int id, CancellationToken ct) =>
        await applications.GetAsync(id, ct) is { } application ? Ok(application) : NotFound();

    /// <summary>Create a new application. Status is always set to Submitted by the server.</summary>
    [HttpPost]
    [ProducesResponseType<LoanApplicationDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LoanApplicationDetailDto>> Create(CreateLoanApplicationRequest request, CancellationToken ct)
    {
        var created = await applications.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Run the deterministic rule engine and store the result. Never approves or rejects.</summary>
    [HttpPost("{id:int}/assess")]
    [ProducesResponseType<AssessmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssessmentDto>> Assess(int id, CancellationToken ct) =>
        await assessments.AssessAsync(id, ct) is { } assessment ? Ok(assessment) : NotFound();
}
