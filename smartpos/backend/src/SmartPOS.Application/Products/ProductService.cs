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
    // Every product has exactly one inventory row (created together), so the null checks only
    // keep the translated SQL a plain LEFT JOIN.
    private static readonly Expression<Func<Product, ProductDto>> ToDto = p => new ProductDto(
        p.Id, p.Sku, p.Barcode, p.Name, p.Description, p.Category, p.Price, p.CostPrice,
        p.Inventory != null ? p.Inventory.StockQuantity : 0,
        p.Inventory != null ? p.Inventory.LowStockThreshold : 0,
        p.Inventory != null && p.Inventory.StockQuantity <= p.Inventory.LowStockThreshold,
        p.ExpiryDate, p.IsActive, p.CreatedAt, p.UpdatedAt);

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    public async Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();
        if (!query.IncludeInactive)
            products = products.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            products = products.Where(p => p.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            products = products.Where(p =>
                EF.Functions.Like(p.Name.ToLower(), pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(p.Sku.ToLower(), pattern, SearchPattern.EscapeCharacter) ||
                (p.Barcode != null && EF.Functions.Like(p.Barcode.ToLower(), pattern, SearchPattern.EscapeCharacter)));
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
        var barcode = Clean(request.Barcode);
        await EnsureSkuIsFree(sku, excludeProductId: null, cancellationToken);
        await EnsureBarcodeIsFree(barcode, excludeProductId: null, cancellationToken);

        var now = calendar.UtcNow;
        var product = new Product
        {
            ShopId = currentUser.ShopId,
            Sku = sku,
            Barcode = barcode,
            Name = request.Name.Trim(),
            Description = Clean(request.Description),
            Category = request.Category.Trim(),
            Price = request.Price,
            CostPrice = request.CostPrice,
            ExpiryDate = request.ExpiryDate,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Inventory = new SmartPOS.Core.Entities.Inventory
            {
                ShopId = currentUser.ShopId,
                StockQuantity = request.InitialStock,
                LowStockThreshold = request.LowStockThreshold,
                LastUpdated = now
            }
        };
        db.Products.Add(product);

        if (request.InitialStock > 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                ShopId = currentUser.ShopId,
                Product = product,
                QuantityChange = request.InitialStock,
                QuantityAfter = request.InitialStock,
                Type = StockMovementType.InitialStock,
                Reason = "Opening stock",
                UserId = currentUser.UserId,
                CreatedAt = now
            });
        }

        // One SaveChanges = one implicit transaction: the product, its inventory row and its
        // opening movement go in together.
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.Include(p => p.Inventory).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw NotFoundException.For("Product", id);

        var sku = NormalizeSku(request.Sku);
        var barcode = Clean(request.Barcode);
        if (sku != product.Sku)
            await EnsureSkuIsFree(sku, product.Id, cancellationToken);
        if (barcode != product.Barcode)
            await EnsureBarcodeIsFree(barcode, product.Id, cancellationToken);

        var now = calendar.UtcNow;
        product.Sku = sku;
        product.Barcode = barcode;
        product.Name = request.Name.Trim();
        product.Description = Clean(request.Description);
        product.Category = request.Category.Trim();
        product.Price = request.Price;
        product.CostPrice = request.CostPrice;
        product.ExpiryDate = request.ExpiryDate;
        product.IsActive = request.IsActive;
        product.UpdatedAt = now;
        if (product.Inventory is { } inventory && inventory.LowStockThreshold != request.LowStockThreshold)
        {
            inventory.LowStockThreshold = request.LowStockThreshold;
            inventory.LastUpdated = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw NotFoundException.For("Product", id);

        if (await db.SaleItems.AnyAsync(i => i.ProductId == id, cancellationToken))
            throw new ConflictException("This product appears on past sales and cannot be deleted. Deactivate it instead.");

        // Its inventory row and stock movements are removed by the database's ON DELETE CASCADE.
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSkuIsFree(string sku, int? excludeProductId, CancellationToken cancellationToken)
    {
        // Friendly pre-check; the unique index on (ShopId, Sku) is the real guarantee.
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeProductId, cancellationToken))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
    }

    private async Task EnsureBarcodeIsFree(string? barcode, int? excludeProductId, CancellationToken cancellationToken)
    {
        if (barcode is not null && await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != excludeProductId, cancellationToken))
            throw new ConflictException($"A product with barcode '{barcode}' already exists.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
