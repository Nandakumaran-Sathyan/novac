using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPOS.Application.Common;
using SmartPOS.Application.Users;

namespace SmartPOS.Application.Auth;

public sealed class AuthService(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    StoreCalendar calendar,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = UserService.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.SimulateVerify(request.Password);
            logger.LogInformation("Login failed: unknown account");
            throw new AuthenticationFailedException();
        }

        var check = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed || !user.IsActive)
        {
            // Same message for wrong password and deactivated account; the log keeps the detail.
            logger.LogInformation("Login failed for user {UserId} (active: {IsActive})", user.Id, user.IsActive);
            throw new AuthenticationFailedException();
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.Hash(request.Password);

        user.LastLoginAt = calendar.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokenService.CreateToken(user);
        logger.LogInformation("User {UserId} logged in", user.Id);
        return new LoginResponse(token, expiresAt, UserService.ToDto(user));
    }
}
