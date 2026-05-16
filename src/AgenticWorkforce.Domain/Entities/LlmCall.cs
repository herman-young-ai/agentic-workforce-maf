namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Record of every LLM API call made by the platform. Written by the
/// BudgetEnforcingChatClient middleware. Partitioned by month in PostgreSQL.
/// </summary>
public class LlmCall : EntityBase
{
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? TaskId { get; set; }
    public Guid? SessionId { get; set; }

    public string AgentName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public long LatencyMs { get; set; }

    /// <summary>HTTP status code from the LLM provider.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Error message if the call failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Hierarchical budget enforcement. Budgets can be scoped to project, session,
/// agent, or execution. BudgetEnforcingChatClient checks before every LLM call.
/// </summary>
public class CostBudget : EntityBase
{
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public BudgetScope Scope { get; set; }

    /// <summary>Scope identifier (e.g., session ID, agent name).</summary>
    public string? ScopeId { get; set; }

    public decimal LimitUsd { get; set; }
    public decimal UsedUsd { get; set; }

    /// <summary>Alert threshold as a fraction (e.g., 0.8 = alert at 80%).</summary>
    public decimal AlertThreshold { get; set; } = 0.8m;

    public bool IsExhausted { get; set; }
}
