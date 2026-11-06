using System.Security.Cryptography;
using System.Text;
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
    IEmailSender emailSender,
    IEmailAddressChecker emailChecker,
    IVerificationCodeHasher codeHasher,
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
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken)
                   ?? throw NotFoundException.For("User", currentUser.UserId);
        if (user.EmailVerifiedAt is not null)
            return; // already done (e.g. a double click): nothing to report

        if (user.EmailVerificationCodeHash is null || user.EmailVerificationExpiresAt <= calendar.UtcNow)
            throw new BusinessRuleException("This code has expired. Request a new one.");

        // Count the try in the database before checking the code, and only while under the limit. Like the stock
        // update, the condition and the change are one statement, so parallel guesses can't get past the limit.
        var counted = await db.Users
            .Where(u => u.Id == user.Id && u.EmailVerificationAttempts < EmailVerificationRules.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificationAttempts, u => u.EmailVerificationAttempts + 1), cancellationToken);
        if (counted == 0)
            throw new BusinessRuleException("Too many wrong codes. Request a new one.");

        var expected = Encoding.ASCII.GetBytes(user.EmailVerificationCodeHash);
        var actual = Encoding.ASCII.GetBytes(codeHasher.Hash(user.Id, request.Code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            var left = Math.Max(EmailVerificationRules.MaxAttempts - user.EmailVerificationAttempts - 1, 0);
            logger.LogInformation("Wrong verification code for user {UserId}", user.Id);
            throw new BusinessRuleException(left > 0
                ? $"That code is not correct. {left} {(left == 1 ? "try" : "tries")} left."
                : "That code is not correct, and that was the last try. Request a new one.");
        }

        user.EmailVerifiedAt = calendar.UtcNow;
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationExpiresAt = null;
        user.EmailVerificationAttempts = 0;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("User {UserId} verified their email", user.Id);
    }

    public async Task ResendVerificationAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken)
                   ?? throw NotFoundException.For("User", currentUser.UserId);
        if (user.EmailVerifiedAt is not null)
            throw new BusinessRuleException("Your email address is already verified.");
        var wait = user.EmailVerificationSentAt + EmailVerificationRules.ResendCooldown - calendar.UtcNow;
        if (wait > TimeSpan.Zero)
            throw new TooManyRequestsException($"A code was just sent. You can ask for another in {Math.Ceiling(wait.Value.TotalSeconds):0} seconds.");
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

    /// <summary>Issues a fresh code (replacing any earlier one) and emails it. A mail failure is logged, never fatal: the user can resend.</summary>
    private async Task SendVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = calendar.UtcNow;
        user.EmailVerificationCodeHash = codeHasher.Hash(user.Id, code);
        user.EmailVerificationExpiresAt = now + EmailVerificationRules.CodeLifetime;
        user.EmailVerificationAttempts = 0;
        user.EmailVerificationSentAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var minutes = (int)EmailVerificationRules.CodeLifetime.TotalMinutes;
        var body = $"Hi {user.FullName},\n\nYour SmartPOS verification code is:\n\n    {code}\n\n" +
                   $"Enter it on the verification screen. It expires in {minutes} minutes and can be tried {EmailVerificationRules.MaxAttempts} times.\n" +
                   "If you didn't create a SmartPOS account, you can ignore this email.";
        try
        {
            await emailSender.SendAsync(user.Email, "Your SmartPOS verification code", body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the verification email to user {UserId}", user.Id);
        }
    }
}
