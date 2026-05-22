using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Filter options for task list queries. Each property is independent and
/// combined with AND when set.
/// </summary>
public record TaskListFilter(
    TaskStatus? Status = null,
    TaskType? Type = null,
    TaskSource? Source = null,
    string? AgentName = null,
    Guid? ParentTaskId = null);

/// <summary>
/// Per-task outcome of a bulk approve attempt. <see cref="Reason"/> is populated
/// only when <see cref="Approved"/> is false.
/// </summary>
public record BulkApproveItem(Guid TaskId, bool Approved, string? Reason);

public record BulkApproveResult(IReadOnlyList<BulkApproveItem> Items);

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
