using Microsoft.AspNetCore.Identity;
using SmartPOS.Application.Auth;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Security;

/// <summary>
/// Adapter over ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>: PBKDF2 with
/// HMAC-SHA512, 100,000 iterations, a random 128-bit salt per password, and a versioned format
/// that reports when a stored hash should be upgraded. We use only the hasher, not the full
/// Identity user/role store.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    // Identity's hasher accepts a user instance for custom hashers; the default one ignores it.
    private static readonly User Unused = new();
    private readonly PasswordHasher<User> _inner = new();
    private readonly string _dummyHash;

    public IdentityPasswordHasher() => _dummyHash = _inner.HashPassword(Unused, Guid.NewGuid().ToString("N"));

    public string Hash(string password) => _inner.HashPassword(Unused, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(Unused, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed
        };

    public void SimulateVerify(string password) => _inner.VerifyHashedPassword(Unused, _dummyHash, password);
}
