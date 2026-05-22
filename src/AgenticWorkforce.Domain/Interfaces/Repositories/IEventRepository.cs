using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Filter for project event queries. All fields are optional and combined
/// with AND. Date bounds are required only when querying outside the default
/// 7-day window (the partition pruner needs explicit bounds for wider scans).
/// </summary>
public record EventFilter(
    EventSeverity? MinSeverity = null,
    string? EventType = null,
    Guid? TaskId = null,
    Guid? SessionId = null,
    DateTime? Since = null,
    DateTime? Until = null);

/// <summary>
/// Query repository for the partitioned project_events table. Append-only;
/// no write or delete methods exposed (events are emitted by the event
/// publisher, not by Api handlers).
/// </summary>
public interface IEventRepository
{
    Task<PagedResult<ProjectEvent>> ListByProjectPagedAsync(
        Guid projectId,
        EventFilter filter,
        PagedQuery paging,
        CancellationToken ct = default);
}
