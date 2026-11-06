using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartPOS.API.Auth;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;

namespace SmartPOS.API.Infrastructure;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "SmartPosWeb";
    public const string LoginRateLimitPolicy = "login";
    public const string AIRateLimitPolicy = "ai";
    public const string ForecastGenerateRateLimitPolicy = "forecast-generate";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICurrentShop, CurrentShop>();
        services.AddSingleton<IVerificationCodeHasher, VerificationCodeHasher>();

        services.AddJwtAuthentication();
        services.AddSwagger();

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Slows password guessing: N login attempts per minute per client IP.
            options.AddPolicy(LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            // AI questions are expensive (one local GPU): N per minute per signed-in user. The endpoint requires a
            // token, so the user id is always present; the IP is only a fallback.
            options.AddPolicy(AIRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(SmartPosClaims.UserId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("Assistant:RequestsPerMinute", 10),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
            // Generating a forecast recomputes the whole shop: a few runs per minute per shop is plenty.
            options.AddPolicy(ForecastGenerateRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(SmartPosClaims.ShopId)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("Forecasting:GeneratePerMinute", 3),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        services.AddHealthChecks()
            // Degraded, not Unhealthy: the shop works without forecasting.
            .AddCheck<ForecastingHealthCheck>("forecasting", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded)
            .AddCheck<DatabaseHealthCheck>("database")
            // Degraded, not Unhealthy: selling works without the AI assistant.
            .AddCheck<AIModelHealthCheck>("ai-model", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
        return services;
    }

    private static void AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep short claim names ("sub", "role")
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateKey(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = SmartPosClaims.Name
                };
                bearer.Events = new JwtBearerEvents
                {
                    // A valid signature isn't enough: reject tokens for users who have since been
                    // deleted or moved to another shop.
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal!;
                        if (!int.TryParse(principal.FindFirst(SmartPosClaims.UserId)?.Value, out var userId) ||
                            !int.TryParse(principal.FindFirst(SmartPosClaims.ShopId)?.Value, out var shopId))
                        {
                            context.Fail("Malformed token.");
                            return;
                        }
                        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
                        // The request has no authenticated user yet, so the shop query filter would match
                        // nothing: switch it off and check the shop claim explicitly instead.
                        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
                            .Where(u => u.Id == userId && u.ShopId == shopId)
                            .Select(u => new { Verified = u.EmailVerifiedAt != null })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (user is null)
                            context.Fail("User no longer exists.");
                        else if (user.Verified)
                            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SmartPosClaims.EmailVerified, "true"));
                    }
                };
            });

        // Secure by default: every endpoint requires a signed-in user with a verified email unless marked
        // [AllowAnonymous] or the SignedIn policy (sign-in status and the verification endpoints).
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .RequireClaim(SmartPosClaims.EmailVerified, "true").Build())
            .AddPolicy(SmartPosPolicies.SignedIn, p => p.RequireAuthenticatedUser());
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, EmailVerificationResultHandler>();
    }

    private static void AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "SmartPOS API",
                Version = "v1",
                Description = "Point-of-sale and inventory API. Sign in with POST /api/auth/login, then click " +
                              "Authorize and paste the accessToken."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT from POST /api/auth/login"
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
            foreach (var xml in Directory.GetFiles(AppContext.BaseDirectory, "SmartPOS.*.xml"))
                options.IncludeXmlComments(xml);
        });
    }

    /// <summary>Health JSON: overall status plus each check's status, without exception details.</summary>
    public static readonly HealthCheckOptions HealthCheckOptions = new()
    {
        ResponseWriter = (context, report) =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
            }));
        }
    };
}

/// <summary>Explains a 403 caused by an unverified email, so the web app can send the user to the code page.</summary>
public sealed class EmailVerificationResultHandler(IProblemDetailsService problemDetails) : IAuthorizationMiddlewareResultHandler
{
    public const string Title = "Email not verified";
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var unverified = authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<ClaimsAuthorizationRequirement>().Any(r => r.ClaimType == SmartPosClaims.EmailVerified) == true;
        if (!unverified)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = 403, Title = Title, Detail = "Confirm your email address with the code we sent you to use SmartPOS." }
        });
    }
}
