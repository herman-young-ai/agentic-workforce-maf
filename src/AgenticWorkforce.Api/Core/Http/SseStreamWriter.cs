using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Core.Http;

/// <summary>
/// SSE response scaffold shared by all stream endpoints. The endpoint
/// resolves authorisation and supplies the channel + per-message transform
/// — everything else (headers, heartbeat, subscribe loop, cleanup) lives
/// here so all three stream endpoints behave identically.
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
    /// Outcome of a per-message transform. <see cref="Skip"/> drops the
    /// message silently (used for in-process filtering, e.g. task-scoped
    /// streams); <see cref="Send"/> emits an SSE frame with the supplied
    /// <c>event:</c> and <c>data:</c> values.
    /// </summary>
    public readonly record struct SseFrame(bool Emit, string EventType, string Payload)
    {
        public static SseFrame Skip { get; } = new(false, string.Empty, string.Empty);
        public static SseFrame Send(string eventType, string payload)
            => new(true, eventType, payload);
    }

    /// <summary>
    /// Writes SSE headers, starts the heartbeat, subscribes to
    /// <paramref name="channel"/>, and dispatches every received message
    /// through <paramref name="transform"/>. Returns when
    /// <paramref name="ct"/> cancels (client disconnect) or the subscription
    /// completes. Single helper used by all stream endpoints so the
    /// header set, heartbeat lifecycle, and cleanup are guaranteed
    /// identical.
    /// </summary>
    public static async Task PumpAsync(
        HttpContext httpContext,
        string channel,
        IRedisPubSubService redisPubSub,
        Func<string, SseFrame> transform,
        CancellationToken ct)
    {
        WriteHeaders(httpContext);
        var heartbeatTask = RunHeartbeatAsync(httpContext, ct);
        try
        {
            await foreach (var msg in redisPubSub.SubscribeAsync(channel, ct))
            {
                var frame = transform(msg);
                if (!frame.Emit) continue;
                await WriteEventAsync(httpContext, frame.EventType, frame.Payload, ct);
            }
        }
        finally
        {
            // RunHeartbeatAsync handles cancellation internally; awaiting
            // surfaces unexpected faults instead of masking them.
            await heartbeatTask;
        }
    }

    private static void WriteHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.ContentType            = "text/event-stream";
        httpContext.Response.Headers.CacheControl           = "no-cache";
        httpContext.Response.Headers.Connection             = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"]   = "no";
    }

    private static async Task WriteEventAsync(
        HttpContext httpContext, string eventType, string jsonPayload, CancellationToken ct)
    {
        await httpContext.Response.WriteAsync(
            $"event: {eventType}\ndata: {jsonPayload}\n\n", ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    private static async Task RunHeartbeatAsync(HttpContext httpContext, CancellationToken ct)
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
