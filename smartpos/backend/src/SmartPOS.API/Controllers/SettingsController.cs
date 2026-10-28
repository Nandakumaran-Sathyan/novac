using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Sales;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/settings")]
[Produces("application/json")]
public sealed class SettingsController(ISaleService saleService) : ControllerBase
{
    /// <summary>Shop name and address, currency, tax rate and the caller's maximum discount.</summary>
    [HttpGet("pos")]
    [ProducesResponseType<PosSettingsDto>(StatusCodes.Status200OK)]
    public Task<PosSettingsDto> Pos(CancellationToken cancellationToken) => saleService.GetPosSettingsAsync(cancellationToken);
}
