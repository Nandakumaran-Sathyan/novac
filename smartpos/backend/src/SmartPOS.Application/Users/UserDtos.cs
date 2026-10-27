using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Users;

public sealed record UserDto(
    int Id,
    string FullName,
    string Email,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public static class PasswordRules
{
    /// <summary>8–128 characters with at least one letter and one digit.</summary>
    public const string Pattern = @"^(?=.*[A-Za-z])(?=.*\d).{8,128}$";
    public const string Message = "Password must be 8-128 characters and contain at least one letter and one digit.";
}

public sealed class CreateUserRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, RegularExpression(PasswordRules.Pattern, ErrorMessage = PasswordRules.Message)]
    public string Password { get; init; } = string.Empty;

    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; init; } = UserRole.Cashier;
}

public sealed class UpdateUserRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Optional. When set, replaces the user's password.</summary>
    [RegularExpression(PasswordRules.Pattern, ErrorMessage = PasswordRules.Message)]
    public string? NewPassword { get; init; }
}

public sealed class UserQuery : PagedQuery
{
    [StringLength(100)]
    public string? Search { get; init; }

    public UserRole? Role { get; init; }
}
