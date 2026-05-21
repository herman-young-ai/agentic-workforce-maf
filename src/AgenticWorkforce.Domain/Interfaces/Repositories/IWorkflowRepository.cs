using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query-only abstraction for the Workflow aggregate (Definitions + Runs).
/// Writes go through <c>AppDbContext.WorkflowDefinitions</c> /
/// <c>AppDbContext.WorkflowRuns</c> directly from vertical-slice handlers.
/// </summary>
public interface IWorkflowRepository
{
    Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default);
}
