using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

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
