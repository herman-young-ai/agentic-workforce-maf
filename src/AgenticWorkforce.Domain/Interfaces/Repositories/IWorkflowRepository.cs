using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query interface for the Workflow aggregate (Definitions + Runs).
/// Phase 3.5 keeps this surface narrow; Phase 4 expands it with workflow CRUD
/// and run listings as new endpoints land.
/// </summary>
public interface IWorkflowRepository
{
    Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default);
}
