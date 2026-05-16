using AgenticWorkforce.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Core.Exceptions;

/// <summary>
/// Central exception handler. Maps domain exceptions to RFC 9457 ProblemDetails.
/// Adopted from SecurityBff reference, extended for agent/audit exceptions.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, code, title) = exception switch
        {
            AppException ex                => (ex.StatusCode, ex.Code, ex.Message),
            DbUpdateException             => (500, ErrorCodes.SysDatabaseError, "A database error occurred."),
            OperationCanceledException    => (499, ErrorCodes.SysServiceUnavailable, "Request was cancelled."),
            _                             => (500, ErrorCodes.SysInternalError, "An unexpected error occurred.")
        };

        if (statusCode >= 500)
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        else
            logger.LogWarning("Handled exception [{Code}]: {Message}", code, exception.Message);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Extensions = { ["code"] = code, ["traceId"] = httpContext.TraceIdentifier }
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
