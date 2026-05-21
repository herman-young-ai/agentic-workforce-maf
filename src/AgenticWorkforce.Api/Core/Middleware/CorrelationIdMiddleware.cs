using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Api.Core.Middleware;

/// <summary>
/// Propagates or generates a correlation ID for every request.
/// Pushed into the logging scope so all log entries within the request
/// carry a CorrelationId property regardless of logging provider.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers.TryAdd(HeaderName, correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
