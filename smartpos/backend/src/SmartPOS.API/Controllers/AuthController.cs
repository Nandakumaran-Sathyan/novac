using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartPOS.API.Infrastructure;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Application.Users;

namespace SmartPOS.API.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService authService, IUserService userService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Exchange email and password for a JWT access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        authService.LoginAsync(request, cancellationToken);

    /// <summary>The signed-in user's profile. Used by the web app to confirm a stored session.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<UserDto> Me(CancellationToken cancellationToken) =>
        userService.GetAsync(currentUser.UserId, cancellationToken);
}
