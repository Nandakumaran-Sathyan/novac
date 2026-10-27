using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Products;

public sealed class ProductService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    StoreCalendar calendar) : IProductService
{
    private static readonly Expression<Func<Product, ProductDto>> ToDto = p => new ProductDto(
        p.Id, p.Sku, p.Name, p.Description, p.CategoryId, p.Category.Name,
        p.SellingPrice, p.CostPrice, p.StockQuantity, p.ReorderLevel,
        p.StockQuantity <= p.ReorderLevel, p.IsActive, p.CreatedAt, p.UpdatedAt);

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    public async Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();
        if (!query.IncludeInactive)
            products = products.Where(p => p.IsActive);
        if (query.CategoryId is { } categoryId)
            products = products.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            products = products.Where(p =>
                EF.Functions.Like(p.Name, pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(p.Sku, pattern, SearchPattern.EscapeCharacter));
        }

        return await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(ToDto)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().Where(p => p.Id == id).Select(ToDto).SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFoundException.For("Product", id);

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var sku = NormalizeSku(request.Sku);
        await EnsureCategoryExists(request.CategoryId, cancellationToken);
        await EnsureSkuIsFree(sku, excludeProductId: null, cancellationToken);

        var now = calendar.UtcNow;
        var product = new Product
        {
            Sku = sku,
            Name = request.Name.Trim(),
            Description = Clean(request.Description),
            CategoryId = request.CategoryId,
            SellingPrice = request.SellingPrice,
            CostPrice = request.CostPrice,
            StockQuantity = request.InitialStock,
            ReorderLevel = request.ReorderLevel,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Products.Add(product);

        if (request.InitialStock > 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                QuantityChange = request.InitialStock,
                QuantityAfter = request.InitialStock,
                Type = StockMovementType.InitialStock,
                Reason = "Opening stock",
                UserId = currentUser.UserId,
                CreatedAt = now
            });
        }

        // One SaveChanges = one implicit transaction: the product and its opening movement go in together.
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw NotFoundException.For("Product", id);

        var sku = NormalizeSku(request.Sku);
        if (request.CategoryId != product.CategoryId)
            await EnsureCategoryExists(request.CategoryId, cancellationToken);
        if (sku != product.Sku)
            await EnsureSkuIsFree(sku, product.Id, cancellationToken);

        product.Sku = sku;
        product.Name = request.Name.Trim();
        product.Description = Clean(request.Description);
        product.CategoryId = request.CategoryId;
        product.SellingPrice = request.SellingPrice;
        product.CostPrice = request.CostPrice;
        product.ReorderLevel = request.ReorderLevel;
        product.IsActive = request.IsActive;
        product.UpdatedAt = calendar.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw NotFoundException.For("Product", id);

        if (await db.SaleItems.AnyAsync(i => i.ProductId == id, cancellationToken))
            throw new ConflictException("This product appears on past sales and cannot be deleted. Deactivate it instead.");

        // Its stock movements are removed by the database's ON DELETE CASCADE.
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCategoryExists(int categoryId, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
            throw new BusinessRuleException($"Category {categoryId} does not exist.");
    }

    private async Task EnsureSkuIsFree(string sku, int? excludeProductId, CancellationToken cancellationToken)
    {
        // Friendly pre-check; the unique index on Sku is the real guarantee.
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeProductId, cancellationToken))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
