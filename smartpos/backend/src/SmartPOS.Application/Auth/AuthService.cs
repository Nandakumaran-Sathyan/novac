using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Auth;

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ISampleDataSeeder sampleDataSeeder,
    ICurrentUser currentUser,
    StoreCalendar calendar,
    ILogger<AuthService> logger) : IAuthService
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.ShopId, u.Shop.ShopName, u.CreatedAt, u.LastLoginAt);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        // The caller is not signed in yet, so there is no shop to scope to: look the email up across all shops.
        var user = await db.Users.IgnoreQueryFilters().Include(u => u.Shop)
            .SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.SimulateVerify(request.Password);
            logger.LogInformation("Login failed: unknown account");
            throw new AuthenticationFailedException();
        }

        var check = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            logger.LogInformation("Login failed for user {UserId}", user.Id);
            throw new AuthenticationFailedException();
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.Hash(request.Password);

        user.LastLoginAt = calendar.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokenService.CreateToken(user);
        logger.LogInformation("User {UserId} logged in", user.Id);
        return new LoginResponse(token, expiresAt, ToDto(user));
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        // Emails are the login name, so they are unique across all shops.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken))
            throw new ConflictException("An account with this email already exists.");

        var now = calendar.UtcNow;
        var shop = new Shop
        {
            ShopName = request.ShopName.Trim(),
            Address = request.ShopAddress?.Trim() ?? string.Empty,
            CreatedAt = now
        };
        var user = new User
        {
            Shop = shop,
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now
        };
        db.Users.Add(user); // adds the shop with it; one SaveChanges = one transaction
        await db.SaveChangesAsync(cancellationToken);
        await sampleDataSeeder.SeedAsync(shop.Id, cancellationToken);

        var (token, expiresAt) = tokenService.CreateToken(user);
        logger.LogInformation("Registered user {UserId} with shop {ShopId}", user.Id, shop.Id);
        return new LoginResponse(token, expiresAt, ToDto(user));
    }

    public async Task<UserDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Shop)
                       .SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken)
                   ?? throw NotFoundException.For("User", currentUser.UserId);
        return ToDto(user);
    }
}
