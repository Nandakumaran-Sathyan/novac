using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Application.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public static class PasswordRules
{
    /// <summary>8–128 characters with at least one letter and one digit.</summary>
    public const string Pattern = @"^(?=.*[A-Za-z])(?=.*\d).{8,128}$";
    public const string Message = "Password must be 8-128 characters and contain at least one letter and one digit.";
}

/// <summary>Creates a new user together with the shop they own.</summary>
public sealed class RegisterRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, RegularExpression(PasswordRules.Pattern, ErrorMessage = PasswordRules.Message)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string ShopName { get; init; } = string.Empty;

    [StringLength(250)]
    public string? ShopAddress { get; init; }
}

public sealed record UserDto(
    int Id,
    string FullName,
    string Email,
    bool EmailVerified,
    int ShopId,
    string ShopName,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed class VerifyEmailRequest
{
    [Required, StringLength(200, MinimumLength = 20)]
    public string Token { get; init; } = string.Empty;
}

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
