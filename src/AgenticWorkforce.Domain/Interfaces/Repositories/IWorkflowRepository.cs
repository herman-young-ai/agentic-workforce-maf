using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

public interface IWorkflowRepository
{
    Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowDefinition> CreateDefinitionAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<WorkflowDefinition> UpdateDefinitionAsync(WorkflowDefinition definition, CancellationToken ct = default);

    Task<WorkflowRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowRun> CreateRunAsync(WorkflowRun run, CancellationToken ct = default);
    Task<WorkflowRun> UpdateRunAsync(WorkflowRun run, CancellationToken ct = default);
}
