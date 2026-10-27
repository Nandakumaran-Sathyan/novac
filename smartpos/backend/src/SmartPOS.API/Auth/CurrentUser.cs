using System.Security.Claims;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.API.Auth;

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Cashier = nameof(UserRole.Cashier);
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No current HTTP request.");

    public int UserId => int.TryParse(Principal.FindFirstValue(SmartPosClaims.UserId), out var id)
        ? id
        : throw new InvalidOperationException("The request is not authenticated.");

    public UserRole Role => Enum.TryParse<UserRole>(Principal.FindFirstValue(SmartPosClaims.Role), out var role)
        ? role
        : throw new InvalidOperationException("The request is not authenticated.");
}
