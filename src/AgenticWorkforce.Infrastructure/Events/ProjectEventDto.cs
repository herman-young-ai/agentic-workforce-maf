using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Wire shape published to Redis pub/sub and forwarded to SignalR/SSE
/// clients. Kept separate from the <c>ProjectEvent</c> entity so the
/// transport payload doesn't drag EF navigation properties or change-tracker
/// state across process boundaries — and so format evolution can be
/// versioned independently of the DB schema.
/// </summary>
public sealed record ProjectEventDto(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    Guid? SessionId,
    string EventType,
    string? Source,
    EventSeverity Severity,
    string? Data,
    DateTime CreatedAt)
{
    /// <summary>
    /// Projects a persisted entity onto the transport shape. Centralised so
    /// every callsite (publisher today, replay/backfill tomorrow) produces
    /// the same mapping — no risk that one consumer forgets a field after a
    /// schema addition.
    /// </summary>
    public static ProjectEventDto From(ProjectEvent evt) => new(
        evt.Id, evt.ProjectId, evt.TaskId, evt.SessionId,
        evt.EventType, evt.Source, evt.Severity, evt.Data, evt.CreatedAt);
}
