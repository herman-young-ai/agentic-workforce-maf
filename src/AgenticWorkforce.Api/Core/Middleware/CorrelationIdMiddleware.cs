namespace AgenticWorkforce.Api.Core.Middleware;

/// <summary>
/// Propagates or generates a correlation ID for every request.
/// Pushed into Serilog LogContext for structured log correlation.
/// Adopted verbatim from SecurityBff reference.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers.TryAdd(HeaderName, correlationId);

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
