using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;

namespace SmartPOS.Infrastructure.Persistence;

/// <summary>
/// `dotnet run --project src/SmartPOS.API -- reset-password --email you@example.com`
/// Sets a new password for an account (there is no forgot-password flow yet). Development only. The password
/// is read from a hidden prompt, or from the NEW_PASSWORD environment variable, so it never appears in the
/// command line or shell history, and it is stored only as a PBKDF2 hash.
/// </summary>
public sealed class ResetPasswordCommand(SmartPosDbContext db, IPasswordHasher hasher, StoreCalendar calendar)
{
    public async Task<string> RunAsync(string email, string newPassword, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(newPassword, PasswordRules.Pattern))
            throw new InvalidOperationException(PasswordRules.Message);

        var normalized = AuthService.NormalizeEmail(email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                   ?? throw new InvalidOperationException($"No account with the email '{normalized}'.");

        user.PasswordHash = hasher.Hash(newPassword);
        user.UpdatedAt = calendar.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return $"Password updated for {normalized}. Existing sign-ins stay valid until their token expires.";
    }
}
