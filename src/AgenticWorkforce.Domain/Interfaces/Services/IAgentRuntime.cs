namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Wraps MAF's ChatClientAgent. The domain never references MAF directly (Principle 4).
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Execute an agent on a task. Returns when the agent completes or fails.
    /// </summary>
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default);
}

public record AgentExecutionRequest(
    Guid ProjectId,
    Guid TaskId,
    string AgentName,
    string Input,
    Guid? SessionId = null);

public record AgentExecutionResult(
    bool Success,
    string? Output,
    string? Error,
    int TokensUsed,
    decimal CostUsd);

/// <summary>
/// Wraps Durable Task SDK. The domain never references DTS directly (Principle 4).
/// </summary>
public interface IWorkflowEngine
{
    Task<Guid> StartWorkflowAsync(Guid projectId, Guid workflowDefinitionId, string? input, CancellationToken ct = default);
    Task PauseWorkflowAsync(Guid executionId, CancellationToken ct = default);
    Task ResumeWorkflowAsync(Guid executionId, CancellationToken ct = default);
    Task CancelWorkflowAsync(Guid executionId, CancellationToken ct = default);
}

/// <summary>
/// Wraps pgvector semantic search + learning CRUD. The domain never references
/// EF Core or pgvector directly (Principle 4).
/// </summary>
public interface IKnowledgeStore
{
    Task<IReadOnlyList<KnowledgeResult>> SearchAsync(
        Guid projectId,
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}

public record KnowledgeResult(
    string Content,
    string Source,
    double Score);
