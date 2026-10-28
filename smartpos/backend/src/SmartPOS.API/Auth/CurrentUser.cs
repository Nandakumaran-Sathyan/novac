using System.Security.Claims;
using SmartPOS.Application.Common;

namespace SmartPOS.API.Auth;

/// <summary>
/// The shop claim of the current request, or null outside a signed-in request. Read by the DbContext's
/// tenant query filters. Note it is null while the JWT is still being validated, which is why token
/// validation looks users up with the filters switched off.
/// </summary>
public sealed class CurrentShop(IHttpContextAccessor httpContextAccessor) : ICurrentShop
{
    public int? ShopId =>
        int.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(SmartPosClaims.ShopId), out var id) ? id : null;
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("No current HTTP request.");

    public int UserId => int.TryParse(Principal.FindFirstValue(SmartPosClaims.UserId), out var id)
        ? id
        : throw new InvalidOperationException("The request is not authenticated.");

    public int ShopId => int.TryParse(Principal.FindFirstValue(SmartPosClaims.ShopId), out var id)
        ? id
        : throw new InvalidOperationException("The request is not authenticated.");

}
