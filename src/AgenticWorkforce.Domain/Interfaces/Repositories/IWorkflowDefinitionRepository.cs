using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for stored workflow definitions (the editable directed graph
/// authored by humans or workflow agents). Deletion is soft — definitions are
/// locked rather than removed so historical runs retain their referenced
/// definition row.
/// </summary>
public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<WorkflowDefinition>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<WorkflowDefinition> AddAsync(WorkflowDefinition definition, CancellationToken ct = default);

    Task UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Soft-delete via setting <see cref="WorkflowDefinition.LockedAt"/>. Once
    /// locked, the definition can still be read by historical runs but cannot
    /// be updated or scheduled. Returns false if no such definition exists.
    /// </summary>
    Task<bool> LockAsync(Guid id, CancellationToken ct = default);
}
