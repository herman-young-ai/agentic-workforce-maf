using System.Text.Json;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Durable + live event publisher.
///
/// <para><b>Durability model</b></para>
/// PostgreSQL <c>project_events</c> is the system of record. Every call
/// persists the row before returning success. Redis pub/sub is a
/// best-effort live transport that fans messages out through SignalR/SSE;
/// failure of the publish step is logged and swallowed so callers do not
/// see phantom errors after a successful DB commit. Clients reconcile any
/// missed live events by paging the events feed with a <c>since</c>
/// cursor on reconnect.
///
/// <para><b>Why this fail-mode</b></para>
/// Throwing after the DB commit would make endpoint handlers roll back
/// their own response — the caller would retry and we'd record the event
/// twice. The chosen mode preserves at-least-once persistence and
/// at-most-once live delivery, which matches what the events feed +
/// SignalR/SSE replay already imply.
/// </summary>
internal sealed class RedisEventPublisher(
    IRedisPubSubService redisPubSub,
    AppDbContext db,
    ILogger<RedisEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync(ProjectEvent evt, CancellationToken ct = default)
    {
        db.ProjectEvents.Add(evt);
        await db.SaveChangesAsync(ct);

        try
        {
            var payload = JsonSerializer.Serialize(
                ProjectEventDto.From(evt), WireJsonOptions.Default);
            await redisPubSub.PublishAsync(RedisChannels.ProjectEvents(evt.ProjectId), payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB row is authoritative — clients replay via the events feed.
            logger.LogWarning(ex,
                "Redis pub/sub failed for {EventType} on project {ProjectId}; "
                + "event persisted, clients will pick it up via the events feed",
                evt.EventType, evt.ProjectId);
        }

        logger.LogInformation(
            "Persisted {EventType} for project {ProjectId}",
            evt.EventType, evt.ProjectId);
    }

    public async Task PublishAsync(
        string channel, string eventType, object data, CancellationToken ct = default)
    {
        // Transient signal — no DB counterpart. Same best-effort posture.
        var payload = JsonSerializer.Serialize(new
        {
            eventType,
            data,
            timestamp = DateTime.UtcNow
        }, WireJsonOptions.Default);

        try
        {
            await redisPubSub.PublishAsync(channel, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Redis pub/sub failed for transient {EventType} on channel {Channel}",
                eventType, channel);
        }
    }
}
