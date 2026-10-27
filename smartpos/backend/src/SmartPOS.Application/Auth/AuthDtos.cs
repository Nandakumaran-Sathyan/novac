using System.ComponentModel.DataAnnotations;
using SmartPOS.Application.Users;

namespace SmartPOS.Application.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
