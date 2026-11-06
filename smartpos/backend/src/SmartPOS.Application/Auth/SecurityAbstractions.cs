using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Auth;

public enum PasswordCheck
{
    Failed,
    Success,
    /// <summary>Correct, but hashed with older parameters; re-hash and save.</summary>
    SuccessRehashNeeded
}

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string passwordHash, string password);

    /// <summary>
    /// Performs a throwaway verification so a login for an unknown email takes about as long as
    /// one with a wrong password (stops attackers discovering which emails exist).
    /// </summary>
    void SimulateVerify(string password);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(User user);
}

/// <summary>
/// Hashes email verification codes with a server-side key. A plain hash of a 6-digit code could be reversed by
/// trying all million codes; a keyed hash can't be without the key. The user id is mixed in, so one user's
/// code is useless for another.
/// </summary>
public interface IVerificationCodeHasher
{
    string Hash(int userId, string code);
}

/// <summary>The rules for email verification codes.</summary>
public static class EmailVerificationRules
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const string CodePattern = @"^\d{6}$";
}
