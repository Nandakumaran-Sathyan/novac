using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Common;

namespace SmartPOS.Application.Products;

public sealed record ProductDto(
    int Id,
    string Sku,
    string Name,
    string? Description,
    int CategoryId,
    string CategoryName,
    decimal SellingPrice,
    decimal CostPrice,
    int StockQuantity,
    int ReorderLevel,
    bool IsLowStock,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Fields shared by create and update.</summary>
public abstract class ProductRequestBase
{
    public const string SkuPattern = @"^[A-Za-z0-9][A-Za-z0-9._-]*$";

    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression(SkuPattern, ErrorMessage = "SKU may contain only letters, digits, '.', '_' and '-'.")]
    public string Sku { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a category.")]
    public int CategoryId { get; init; }

    [Range(typeof(decimal), "0", "9999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal SellingPrice { get; init; }

    [Range(typeof(decimal), "0", "9999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal CostPrice { get; init; }

    [Range(0, 1_000_000)]
    public int ReorderLevel { get; init; }
}

public sealed class CreateProductRequest : ProductRequestBase
{
    /// <summary>Opening stock. Recorded as an InitialStock movement.</summary>
    [Range(0, 1_000_000)]
    public int InitialStock { get; init; }
}

/// <summary>Stock quantity is deliberately absent: change stock through inventory adjustments.</summary>
public sealed class UpdateProductRequest : ProductRequestBase
{
    public bool IsActive { get; init; } = true;
}

public sealed class ProductQuery : PagedQuery
{
    /// <summary>Matches name or SKU.</summary>
    [StringLength(100)]
    public string? Search { get; init; }

    public int? CategoryId { get; init; }

    /// <summary>Default true; the POS passes false to hide discontinued products.</summary>
    public bool IncludeInactive { get; init; } = true;
}
