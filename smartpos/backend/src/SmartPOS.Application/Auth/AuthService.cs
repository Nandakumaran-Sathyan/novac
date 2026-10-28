using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Auth;

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ISampleDataSeeder sampleDataSeeder,
    IEmailSender emailSender,
    IEmailAddressChecker emailChecker,
    IOptions<AppOptions> appOptions,
    ICurrentUser currentUser,
    StoreCalendar calendar,
    ILogger<AuthService> logger) : IAuthService
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.EmailVerifiedAt != null, u.ShopId, u.Shop.ShopName, u.CreatedAt, u.LastLoginAt);

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
        await EnsureDeliverableAsync(email, cancellationToken);

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
        await SendVerificationAsync(user, cancellationToken);

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

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        // The token is "<user id>.<random secret>". Only the secret's hash is stored, and the comparison is constant-time.
        var parts = request.Token.Split('.', 2);
        var user = parts.Length == 2 && int.TryParse(parts[0], out var userId)
            ? await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : null;

        var valid = user?.EmailVerificationTokenHash is { } stored &&
                    CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(HashToken(parts[1]))) &&
                    user.EmailVerificationExpiresAt > calendar.UtcNow;
        if (!valid)
            throw new BusinessRuleException("This verification link is invalid or has expired. Sign in and request a new one.");

        user!.EmailVerifiedAt ??= calendar.UtcNow;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("User {UserId} verified their email", user.Id);
    }

    public async Task ResendVerificationAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken)
                   ?? throw NotFoundException.For("User", currentUser.UserId);
        if (user.EmailVerifiedAt is not null)
            throw new BusinessRuleException("Your email address is already verified.");
        await SendVerificationAsync(user, cancellationToken);
    }

    /// <summary>
    /// Rejects addresses that cannot work: throwaway-mailbox domains, and domains that do not exist or have no
    /// mail server (typos like "gmial.com"). A failed lookup is not held against the user.
    /// </summary>
    private async Task EnsureDeliverableAsync(string email, CancellationToken cancellationToken)
    {
        var domain = email[(email.LastIndexOf('@') + 1)..];
        if (DisposableEmailDomains.IsDisposable(domain))
            throw new BusinessRuleException("Please use a permanent email address; temporary-mailbox services aren't accepted.");

        var status = await emailChecker.CheckDomainAsync(domain, cancellationToken);
        if (status == EmailDomainStatus.CannotReceiveMail)
            throw new BusinessRuleException($"'{domain}' doesn't appear to accept email. Check the address for typos.");
        if (status == EmailDomainStatus.Unknown)
            logger.LogWarning("Could not check mail servers for {Domain}; allowing the registration", domain);
    }

    /// <summary>Issues a fresh single-use token and emails the link. A mail failure is logged, never fatal: the user can resend.</summary>
    private async Task SendVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var options = appOptions.Value;
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        user.EmailVerificationTokenHash = HashToken(secret);
        user.EmailVerificationExpiresAt = calendar.UtcNow.AddHours(options.EmailVerificationHours);
        await db.SaveChangesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(options.WebBaseUrl))
        {
            logger.LogWarning("App:WebBaseUrl is not set, so no verification link can be emailed to user {UserId}", user.Id);
            return;
        }
        var link = $"{options.WebBaseUrl.TrimEnd('/')}/verify-email?token={user.Id}.{secret}";
        var body = $"Hi {user.FullName},\n\nConfirm your email address for SmartPOS by opening this link:\n{link}\n\n" +
                   $"It works once and expires in {options.EmailVerificationHours} hours. If you didn't create an account, you can ignore this email.";
        try
        {
            await emailSender.SendAsync(user.Email, "Confirm your SmartPOS email address", body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the verification email to user {UserId}", user.Id);
        }
    }

    private static string HashToken(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
