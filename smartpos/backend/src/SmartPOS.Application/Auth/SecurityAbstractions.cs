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
