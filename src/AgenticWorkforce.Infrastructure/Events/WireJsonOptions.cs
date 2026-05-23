using System.Text.Json;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Single <see cref="JsonSerializerOptions"/> instance shared by every
/// serializer that writes data to (or reads data from) a wire that crosses
/// a process boundary — Redis pub/sub payloads, SignalR push frames, SSE
/// frames, the idempotency cache, and the SSE-token snapshot.
///
/// <para><b>Why one shared instance</b></para>
/// Before this existed, each call site invoked
/// <c>JsonSerializer.Serialize(value)</c> with the framework default
/// options (PascalCase). ASP.NET Core's REST responses, however, use
/// <see cref="JsonSerializerDefaults.Web"/> (camelCase). The result was
/// two JSON shapes for the same data depending on the transport —
/// browsers reading <c>GET /api/v1/.../events</c> got <c>eventType</c>
/// while clients subscribed to the same project's SignalR hub got
/// <c>EventType</c>. Routing every wire-bound serializer through this
/// options instance guarantees the contract is identical regardless of
/// transport.
///
/// <para><b>Caching</b></para>
/// <see cref="JsonSerializerOptions"/> caches reflection metadata across
/// uses; a long-lived shared instance is materially faster than allocating
/// per-call. Microsoft's guidance: build once, reuse forever.
/// </summary>
public static class WireJsonOptions
{
    /// <summary>
    /// Use this for every <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/>
    /// and <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
    /// call that targets a Redis or SignalR/SSE payload.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
