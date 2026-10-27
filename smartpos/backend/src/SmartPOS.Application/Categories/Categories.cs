using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Categories;

public sealed record CategoryDto(int Id, string Name, string? Description, int ProductCount);

public sealed class CreateCategoryRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);
}

public sealed class CategoryService(IApplicationDbContext db, StoreCalendar calendar) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.Products.Count))
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var lowered = name.ToLower();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == lowered, cancellationToken))
            throw new ConflictException($"Category '{name}' already exists.");

        var category = new Category
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = calendar.UtcNow
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Description, 0);
    }
}
