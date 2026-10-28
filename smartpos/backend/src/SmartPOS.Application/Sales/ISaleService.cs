using SmartPOS.Application.Common;

namespace SmartPOS.Application.Sales;

public interface ISaleService
{
    Task<SaleDto> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken);
    Task<PagedResult<SaleSummaryDto>> ListAsync(SaleQuery query, CancellationToken cancellationToken);
    Task<SaleDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<PosSettingsDto> GetPosSettingsAsync(CancellationToken cancellationToken);
}
