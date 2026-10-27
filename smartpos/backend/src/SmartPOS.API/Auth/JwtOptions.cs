using System.ComponentModel.DataAnnotations;

namespace SmartPOS.API.Auth;

/// <summary>Bound from "Jwt". SigningKey must come from user-secrets or an environment variable.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "SmartPOS.Api";

    [Required]
    public string Audience { get; set; } = "SmartPOS.Web";

    /// <summary>HMAC-SHA256 key. At least 32 characters (256 bits); generate with `openssl rand -base64 48`.</summary>
    [Required(ErrorMessage = "Jwt:SigningKey is not configured. See README > Configuration.")]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Token lifetime. Default covers one shift; users sign in again afterwards.</summary>
    [Range(5, 1440)]
    public int ExpiryMinutes { get; set; } = 480;
}
