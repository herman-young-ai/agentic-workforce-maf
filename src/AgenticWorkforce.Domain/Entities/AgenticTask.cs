namespace AgenticWorkforce.Domain.Entities;

public class AgenticTask : ProjectScopedEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public TaskType Type { get; set; } = TaskType.AgentTask;

    public Guid? AssignedAgentId { get; set; }
    public AgentCatalog? AssignedAgent { get; set; }

    public Guid? WorkflowNodeId { get; set; }

    /// <summary>Self-referencing parent for task decomposition.</summary>
    public Guid? ParentTaskId { get; set; }
    public AgenticTask? ParentTask { get; set; }

    /// <summary>Task input payload (jsonb).</summary>
    public string? Input { get; set; }

    /// <summary>Task output payload (jsonb).</summary>
    public string? Output { get; set; }

    public bool RequiresApproval { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public ICollection<AgenticTask> SubTasks { get; set; } = [];
    public ICollection<TaskAttempt> Attempts { get; set; } = [];
    public ICollection<TaskDependency> Dependencies { get; set; } = [];
    public ICollection<TaskDependency> Dependents { get; set; } = [];
    public ICollection<Decision> Decisions { get; set; } = [];
    public ICollection<Artifact> Artifacts { get; set; } = [];
}

public class TaskAttempt : TaskScopedEntity
{
    public int AttemptNumber { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.Running;

    /// <summary>Attempt input snapshot (jsonb).</summary>
    public string? Input { get; set; }

    /// <summary>Attempt output (jsonb).</summary>
    public string? Output { get; set; }

    public string? Error { get; set; }
    public int TokensUsed { get; set; }
    public decimal CostUsd { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Directed dependency between tasks. TaskId depends on DependsOnTaskId.
/// </summary>
public class TaskDependency
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;

    public Guid DependsOnTaskId { get; set; }
    public AgenticTask DependsOnTask { get; set; } = null!;
}
