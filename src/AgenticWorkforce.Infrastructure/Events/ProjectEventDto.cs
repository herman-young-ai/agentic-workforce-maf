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
    DateTime CreatedAt);
