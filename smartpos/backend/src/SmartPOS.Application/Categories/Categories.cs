using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;

namespace SmartPOS.Application.Categories;

public sealed record CategoryDto(string Name, int ProductCount);

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A category is just the text on a product, so this lists the distinct names in use (with how many
/// products use each) to fill filter dropdowns and suggestions. A new category exists as soon as a
/// product is saved with it.
/// </summary>
public sealed class CategoryService(IApplicationDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Select(g => new CategoryDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
}
