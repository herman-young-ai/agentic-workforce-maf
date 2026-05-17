namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Wraps the agent execution runtime. The domain never references MAF directly
/// (Principle 4). The richer ProjectContext / streaming interface lives inside
/// AgenticWorkforce.Agents and is internal to that project.
/// </summary>
public interface IAgentRuntime
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default);
}

public record AgentExecutionRequest(
    Guid ProjectId,
    Guid TaskId,
    string AgentName,
    string Objective,
    string? Input = null,
    Guid? SessionId = null,
    TimeSpan? Timeout = null);

public record AgentExecutionResult(
    bool Success,
    string? Output,
    string? Error,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double DurationSeconds,
    int ToolCallCount);
