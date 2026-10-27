using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SmartPOS.API.Auth;
using SmartPOS.Application.Auth;
using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.API.Infrastructure;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "SmartPosWeb";
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

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
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
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
                    NameClaimType = SmartPosClaims.Name,
                    RoleClaimType = SmartPosClaims.Role
                };
                bearer.Events = new JwtBearerEvents
                {
                    // A valid signature isn't enough: reject tokens for users who have since been
                    // deactivated or had their role changed, so those changes take effect immediately.
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal!;
                        if (!int.TryParse(principal.FindFirst(SmartPosClaims.UserId)?.Value, out var userId) ||
                            !Enum.TryParse<UserRole>(principal.FindFirst(SmartPosClaims.Role)?.Value, out var role))
                        {
                            context.Fail("Malformed token.");
                            return;
                        }
                        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
                        var stillValid = await db.Users.AsNoTracking()
                            .AnyAsync(u => u.Id == userId && u.IsActive && u.Role == role, context.HttpContext.RequestAborted);
                        if (!stillValid)
                            context.Fail("User is inactive or their role has changed.");
                    }
                };
            });

        // Secure by default: every endpoint requires a signed-in user unless marked [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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
