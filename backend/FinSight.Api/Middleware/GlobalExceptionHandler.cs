using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails responses. Full details
/// go to the server log; clients get a safe message and a traceId to quote in support requests,
/// never a stack trace or SQL text.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        // Client disconnected mid-request: nothing useful to send back.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return true;

        var (status, title) = IsDatabaseUnavailable(exception)
            ? (StatusCodes.Status503ServiceUnavailable, "The database is currently unavailable. Please try again shortly.")
            : (StatusCodes.Status500InternalServerError, "An unexpected error occurred.");

        logger.LogError(exception, "Unhandled exception for {Method} {Path} → {Status}",
            httpContext.Request.Method, httpContext.Request.Path, status);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }

    // A SqlException raised outside SaveChanges (i.e. not wrapped in DbUpdateException) is
    // almost always a connectivity or login failure rather than a data problem.
    // Verified: with SQL Server stopped, EF Core queries surface a bare SqlException.
    private static bool IsDatabaseUnavailable(Exception exception) =>
        exception is SqlException
        || (exception is not DbUpdateException && exception.InnerException is SqlException);
}
