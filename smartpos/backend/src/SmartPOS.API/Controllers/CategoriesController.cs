using Microsoft.AspNetCore.Mvc;
using SmartPOS.Application.Categories;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>Distinct product categories in use, with product counts. A category is created by saving a product with its name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken cancellationToken) =>
        categoryService.ListAsync(cancellationToken);
}
