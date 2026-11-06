namespace SmartPOS.Core.Entities;

/// <summary>
/// A person who signs in. There is one kind of account: each user owns exactly one shop (created
/// when they register) and sees only that shop's customers, products, stock and sales.
/// </summary>
public class User
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public Shop Shop { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;
    /// <summary>Stored lower-cased so the unique index is case-insensitive on every database. Unique across all shops because it is the login name.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>ASP.NET Core Identity PBKDF2 hash. The plaintext password is never stored.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>When the user proved they own the email address by entering the code we sent. Null until then.</summary>
    public DateTime? EmailVerifiedAt { get; set; }
    /// <summary>Keyed hash (HMAC-SHA256, hex) of the pending 6-digit code; the code itself is never stored.</summary>
    public string? EmailVerificationCodeHash { get; set; }
    public DateTime? EmailVerificationExpiresAt { get; set; }
    /// <summary>Tries made against the pending code; at the limit the code is dead and a new one must be sent.</summary>
    public int EmailVerificationAttempts { get; set; }
    /// <summary>When the pending code was sent; limits how often a new one can be requested.</summary>
    public DateTime? EmailVerificationSentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
