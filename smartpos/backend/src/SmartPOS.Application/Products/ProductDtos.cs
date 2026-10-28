using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Common;

namespace SmartPOS.Application.Products;

public sealed record ProductDto(
    int Id,
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    string Category,
    decimal Price,
    decimal CostPrice,
    int StockQuantity,
    int LowStockThreshold,
    bool IsLowStock,
    DateOnly? ExpiryDate,
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

    /// <summary>Manufacturer's scannable barcode (digits only, as printed on the pack).</summary>
    [StringLength(50)]
    [RegularExpression(@"^\d*$", ErrorMessage = "Barcode may contain only digits.")]
    public string? Barcode { get; init; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    /// <summary>Free text; products with the same category name are grouped together.</summary>
    [Required, StringLength(100, MinimumLength = 2)]
    public string Category { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal Price { get; init; }

    [Range(typeof(decimal), "0", "9999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal CostPrice { get; init; }

    /// <summary>Stock at or below this level is flagged as low.</summary>
    [Range(0, 1_000_000)]
    public int LowStockThreshold { get; init; } = 20;

    public DateOnly? ExpiryDate { get; init; }
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
    /// <summary>Matches name, SKU or barcode.</summary>
    [StringLength(100)]
    public string? Search { get; init; }

    /// <summary>Exact category name.</summary>
    [StringLength(100)]
    public string? Category { get; init; }

    /// <summary>Default true; the POS passes false to hide discontinued products.</summary>
    public bool IncludeInactive { get; init; } = true;
}
