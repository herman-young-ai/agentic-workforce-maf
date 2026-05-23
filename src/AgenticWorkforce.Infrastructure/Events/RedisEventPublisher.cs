using System.Text.Json;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Durable + live event publisher (transactional outbox).
///
/// <para><b>Durability model</b></para>
/// PostgreSQL <c>project_events</c> is the system of record. The
/// <see cref="ProjectEvent"/> overload only ADDS to the active DbContext;
/// the caller's existing <c>SaveChanges</c> (typically inside their
/// repository's mutation method) flushes the event in the SAME
/// transaction as the business change. Redis pub/sub dispatch happens in
/// <see cref="ProjectEventDispatchInterceptor"/> AFTER the commit
/// succeeds. If pub/sub fails the DB row is still authoritative and
/// clients reconcile via the events feed on reconnect.
///
/// <para><b>Caller contract</b></para>
/// Calling <see cref="PublishAsync(ProjectEvent, CancellationToken)"/>
/// MUST be followed (or preceded — order within a unit of work doesn't
/// matter) by a <c>SaveChanges</c> on the same scoped DbContext.
/// Endpoint handlers get this for free via their repository methods.
/// Direct callers (tests, background workers operating outside a
/// repository) must explicitly save.
///
/// <para><b>Transient overload</b></para>
/// <see cref="PublishAsync(string, string, object, CancellationToken)"/>
/// is the no-DB-counterpart path: pure pub/sub fan-out for signals like
/// "agent token chunk" that have no durable representation.
/// </summary>
internal sealed class RedisEventPublisher(
    IRedisPubSubService redisPubSub,
    AppDbContext db,
    ILogger<RedisEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(ProjectEvent evt, CancellationToken ct = default)
    {
        // Add only — the DbContext's SaveChanges (driven by the caller)
        // commits the row, then ProjectEventDispatchInterceptor's
        // post-commit hook fans out via Redis pub/sub.
        db.ProjectEvents.Add(evt);
        return Task.CompletedTask;
    }

    public async Task PublishAsync(
        string channel, string eventType, object data, CancellationToken ct = default)
    {
        // Transient signal — no DB counterpart, no transaction to attach
        // to. Same best-effort posture: failures are logged, not thrown.
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
