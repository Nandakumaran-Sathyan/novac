using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Dashboard;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(IDashboardService dashboardService, IInsightsService insightsService) : ControllerBase
{
    /// <summary>Today's totals (store time zone), catalogue counts, recent sales and low-stock products.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public Task<DashboardSummaryDto> Summary(CancellationToken cancellationToken) =>
        dashboardService.GetSummaryAsync(cancellationToken);

    /// <summary>
    /// Sales analytics for the last <paramref name="days"/> store days (1–180, default 30): KPIs compared with the
    /// previous period, a daily series, a weekday × hour heatmap, payment mix, categories, top products and customers.
    /// </summary>
    [HttpGet("insights")]
    [ProducesResponseType<InsightsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<InsightsDto> Insights([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        insightsService.GetAsync(days, cancellationToken);
}
