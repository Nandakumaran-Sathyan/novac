using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPOS.API.Auth;
using SmartPOS.Application.Reports;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Admin)]
[Produces("application/json")]
public sealed class ReportsController(IReportService reportService) : ControllerBase
{
    /// <summary>Per-day sales totals (store time zone). Defaults to the last 7 days; max 366 days.</summary>
    /// <param name="from">First day, inclusive (yyyy-MM-dd).</param>
    /// <param name="to">Last day, inclusive (yyyy-MM-dd).</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("daily-sales")]
    [ProducesResponseType<DailySalesReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<DailySalesReport> DailySales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        reportService.GetDailySalesAsync(from, to, cancellationToken);

    /// <summary>Best-selling products by revenue (line totals before sale-level discount).</summary>
    [HttpGet("top-products")]
    [ProducesResponseType<IReadOnlyList<TopProductRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<TopProductRow>> TopProducts(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int limit = 10, CancellationToken cancellationToken = default) =>
        reportService.GetTopProductsAsync(from, to, limit, cancellationToken);
}
