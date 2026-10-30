using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.AI;
using SmartPOS.Application.Common;

namespace SmartPOS.API.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 7807 problem responses. Expected failures (<see cref="AppException"/>)
/// return their message; anything else returns a generic 500 with only a trace ID, and the full
/// exception is written to the server log.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BusinessRuleException => (StatusCodes.Status400BadRequest, "Request not allowed", exception.Message),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Authentication failed", exception.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            TooManyRequestsException => (StatusCodes.Status429TooManyRequests, "Too many requests", exception.Message),
            AIUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AI assistant unavailable", exception.Message),
            // Typically a unique index or FK violation that slipped past a pre-check because of a race.
            DbUpdateException => (StatusCodes.Status409Conflict, "Conflict", "The change conflicts with existing data. Reload and try again."),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.")
        };

        if (exception is AIUnavailableException)
            logger.LogWarning("AI unavailable for {Method} {Path}: {Message}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        else if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else if (exception is DbUpdateException)
            logger.LogWarning(exception, "Database update conflict for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogInformation("{Status} for {Method} {Path}: {Message}", status, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
