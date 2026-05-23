namespace AgenticWorkforce.Api.Core.Http;

/// <summary>
/// SSE response scaffold shared by all stream endpoints. Each endpoint
/// resolves what to subscribe to and what to write — the headers,
/// heartbeat, and flushing are common.
///
/// <para><b>Why a heartbeat</b></para>
/// Idle SSE connections get dropped by middleboxes (Nginx, Azure Front
/// Door) at 30–60 s. A <c>: ping\n\n</c> comment line every 15 s is a
/// valid SSE comment per the spec — it never surfaces to client code,
/// just keeps the TCP connection warm.
///
/// <para><b>Why X-Accel-Buffering: no</b></para>
/// Hint to reverse proxies (especially Nginx) to forward the response
/// stream immediately rather than buffer until a chunk fills.
/// </summary>
public static class SseStreamWriter
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Writes SSE response headers — call once before the first event.
    /// </summary>
    public static void WriteHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.ContentType            = "text/event-stream";
        httpContext.Response.Headers.CacheControl           = "no-cache";
        httpContext.Response.Headers.Connection             = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"]   = "no";
    }

    /// <summary>
    /// Writes a single SSE event frame. <paramref name="eventType"/> maps
    /// to the EventSource <c>event.type</c> on the client; the JSON-encoded
    /// payload is the <c>event.data</c>.
    /// </summary>
    public static async Task WriteEventAsync(
        HttpContext httpContext, string eventType, string jsonPayload, CancellationToken ct)
    {
        await httpContext.Response.WriteAsync(
            $"event: {eventType}\ndata: {jsonPayload}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Runs a periodic <c>: ping</c> heartbeat alongside whatever event
    /// loop the caller is driving. The returned Task completes when
    /// <paramref name="ct"/> is cancelled — typically when the consumer
    /// disconnects. Catch <see cref="OperationCanceledException"/> at the
    /// call site or pass through; either is fine.
    /// </summary>
    public static async Task RunHeartbeatAsync(HttpContext httpContext, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await httpContext.Response.WriteAsync(": ping\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client disconnect, normal */ }
    }
}
