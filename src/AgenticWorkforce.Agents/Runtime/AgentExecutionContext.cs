namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Internal value object that carries per-execution context across the agent
/// factory, prompt assembler, and middleware. Distinct from
/// <see cref="AgenticWorkforce.Domain.Entities.ProjectContext"/> (the PCD entity)
/// to avoid name collision.
/// </summary>
internal sealed record AgentExecutionContext(
    Guid ProjectId,
    Guid TaskId,
    Guid? SessionId,
    string AgentName,
    string Objective,
    string? Input);
