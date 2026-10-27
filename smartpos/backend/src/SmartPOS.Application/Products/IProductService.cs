using SmartPOS.Application.Common;

namespace SmartPOS.Application.Products;

public interface IProductService
{
    Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken cancellationToken);
    Task<ProductDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
