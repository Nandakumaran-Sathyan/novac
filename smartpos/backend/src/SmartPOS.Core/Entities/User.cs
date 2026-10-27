using SmartPOS.Core.Enums;

namespace SmartPOS.Core.Entities;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>Stored lower-cased so the unique index is case-insensitive on every collation.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>ASP.NET Core Identity PBKDF2 hash. The plaintext password is never stored.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
