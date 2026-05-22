using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query-only repository for workflow runs. Writes happen in the Worker
/// when it consumes the execution queue (Phase 8) — Api never creates
/// WorkflowRun rows directly, only enqueues dispatch messages.
/// </summary>
public interface IWorkflowRunRepository
{
    Task<WorkflowRun?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<WorkflowRun>> ListByProjectPagedAsync(
        Guid projectId,
        Guid? workflowDefinitionId,
        PagedQuery paging,
        CancellationToken ct = default);
}
