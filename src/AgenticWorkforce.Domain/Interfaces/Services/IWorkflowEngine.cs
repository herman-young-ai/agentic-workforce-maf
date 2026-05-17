namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Wraps the workflow execution engine (Durable Task SDK). The domain never
/// references DTS directly (Principle 4).
/// </summary>
public interface IWorkflowEngine
{
    Task<Guid> StartAsync(
        Guid projectId,
        Guid workflowDefinitionId,
        string? triggerType,
        string? context,
        CancellationToken ct = default);

    Task PauseAsync(Guid workflowRunId, CancellationToken ct = default);
    Task ResumeAsync(Guid workflowRunId, CancellationToken ct = default);
    Task CancelAsync(Guid workflowRunId, CancellationToken ct = default);

    Task SubmitHumanInputAsync(
        Guid requestId,
        string response,
        Guid responderId,
        CancellationToken ct = default);
}
