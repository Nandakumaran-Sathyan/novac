using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Users;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Enums;

namespace SmartPOS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Insert sample categories and products into an empty catalogue. Development only.</summary>
    public bool SeedSampleData { get; set; }
}

/// <summary>
/// Creates the first administrator from configuration, so no account with a known password is
/// ever baked into the code or migrations. Supply both values through user-secrets or environment
/// variables (Bootstrap__AdminEmail / Bootstrap__AdminPassword); it runs only while no admin
/// exists, so remove the password from the environment once the account is created.
/// </summary>
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "Administrator";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AdminEmail) && !string.IsNullOrWhiteSpace(AdminPassword);
}

/// <summary>
/// Runs once at startup. It never creates or migrates the schema; that is done explicitly with
/// `dotnet ef database update`, so production schema changes are always deliberate.
/// </summary>
public sealed class DatabaseInitializer(
    SmartPosDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<BootstrapAdminOptions> bootstrapOptions,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var seed = databaseOptions.Value.SeedSampleData;
        var bootstrap = bootstrapOptions.Value;
        if (!seed && !bootstrap.IsConfigured)
            return;

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogWarning("Database is not reachable; skipping startup seeding. Check ConnectionStrings:SmartPos.");
                return;
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogWarning("{Count} pending EF Core migration(s); skipping startup seeding. Run `dotnet ef database update`.", pending.Count);
                return;
            }

            if (bootstrap.IsConfigured)
                await EnsureAdminAsync(bootstrap, cancellationToken);
            if (seed)
                await SeedSampleDataAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't stop the API from starting (the /health endpoint will report the database problem).
            logger.LogError(ex, "Startup database initialisation failed");
        }
    }

    private async Task EnsureAdminAsync(BootstrapAdminOptions bootstrap, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
            return;

        if (!Regex.IsMatch(bootstrap.AdminPassword!, PasswordRules.Pattern))
        {
            logger.LogError("Bootstrap admin not created: {Rule}", PasswordRules.Message);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var admin = new User
        {
            FullName = bootstrap.AdminFullName.Trim(),
            Email = UserService.NormalizeEmail(bootstrap.AdminEmail!),
            PasswordHash = passwordHasher.Hash(bootstrap.AdminPassword!),
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Created bootstrap administrator (user {UserId}). Remove Bootstrap:AdminPassword from configuration.", admin.Id);
    }

    private async Task SeedSampleDataAsync(CancellationToken cancellationToken)
    {
        if (await db.Categories.AnyAsync(cancellationToken) || await db.Products.AnyAsync(cancellationToken))
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var categories = SampleData.Categories.ToDictionary(
            c => c.Name,
            c => new Category { Name = c.Name, Description = c.Description, CreatedAt = now });
        db.Categories.AddRange(categories.Values);

        foreach (var p in SampleData.Products)
        {
            var product = new Product
            {
                Sku = p.Sku,
                Name = p.Name,
                Category = categories[p.Category],
                SellingPrice = p.SellingPrice,
                CostPrice = p.CostPrice,
                StockQuantity = p.Stock,
                ReorderLevel = p.ReorderLevel,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Products.Add(product);
            if (p.Stock > 0)
            {
                db.StockMovements.Add(new StockMovement
                {
                    Product = product,
                    QuantityChange = p.Stock,
                    QuantityAfter = p.Stock,
                    Type = StockMovementType.InitialStock,
                    Reason = "Sample data opening stock",
                    CreatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Categories} sample categories and {Products} sample products",
            categories.Count, SampleData.Products.Count);
    }
}
