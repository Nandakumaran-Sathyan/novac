using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Dashboard;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>Today's totals (store time zone), catalogue counts, recent sales and low-stock products.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    public Task<DashboardSummaryDto> Summary(CancellationToken cancellationToken) =>
        dashboardService.GetSummaryAsync(cancellationToken);
}
