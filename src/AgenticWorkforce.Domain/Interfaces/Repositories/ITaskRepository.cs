using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the AgenticTask aggregate.
/// </summary>
public interface ITaskRepository
{
    Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<AgenticTask>> ListByProjectAsync(
        Guid projectId,
        TaskStatus? statusFilter = null,
        CancellationToken ct = default);

    Task<PagedResult<AgenticTask>> ListByProjectPagedAsync(
        Guid projectId,
        TaskListFilter filter,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgenticTask>> GetBoardAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent terminal tasks (Completed, Failed, Cancelled) for a
    /// project, ordered by <c>CompletedAt</c> descending and limited to
    /// <paramref name="count"/>. Backs <c>project.get_recent_outcomes</c>: the consumer
    /// always wants the latest resolved outcomes, regardless of how many in-flight
    /// tasks sit in front of them on a creation-time ordering.
    /// </summary>
    Task<IReadOnlyList<AgenticTask>> ListRecentOutcomesAsync(
        Guid projectId,
        int count,
        CancellationToken ct = default);

    /// <summary>
    /// Counts tasks in a project, optionally restricted to a set of statuses
    /// (e.g. ["Approved", "Queued", "Running"] for "active task count").
    /// </summary>
    Task<int> CountByProjectAsync(
        Guid projectId,
        IReadOnlyList<TaskStatus>? statuses = null,
        CancellationToken ct = default);

    Task<AgenticTask> AddAsync(AgenticTask task, CancellationToken ct = default);

    Task UpdateAsync(AgenticTask task, CancellationToken ct = default);

    /// <summary>
    /// Approve a batch of proposed tasks atomically. Each task is checked for
    /// proper status (must be Proposed) and segregation of duties (approver must
    /// not be the task creator). Per-task failures are reported in the result;
    /// only the successful tasks are saved.
    /// </summary>
    Task<BulkApproveResult> BulkApproveAsync(
        Guid projectId,
        IReadOnlyList<Guid> taskIds,
        Guid approverId,
        CancellationToken ct = default);
}
