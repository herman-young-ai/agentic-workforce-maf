using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class AgenticTask : ProjectScopedEntity
{
    public TaskType Type { get; set; }
    public TaskStatus Status { get; set; }
    public string Objective { get; set; } = null!;
    public string? AgentName { get; set; }
    public TaskSource Source { get; set; }
    public string? WorkflowNodeId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public AgenticTask? ParentTask { get; set; }
    public Guid? WorkflowRunId { get; set; }
    public WorkflowRun? WorkflowRun { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Inputs { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Outputs { get; set; }

    public string? OutputSummary { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    public Guid? AssignedToId { get; set; }
    public User? AssignedTo { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    // Navigation
    public ICollection<AgenticTask> ChildTasks { get; set; } = [];
    public ICollection<TaskAttempt> Attempts { get; set; } = [];
    public ICollection<TaskDependency> Dependencies { get; set; } = [];
    public ICollection<TaskDependency> Dependents { get; set; } = [];
    public ICollection<ProjectArtifact> Artifacts { get; set; } = [];
    public ICollection<ProjectEvent> Events { get; set; } = [];
    public ICollection<ProjectLearning> Learnings { get; set; } = [];
    public ICollection<ProjectDecision> Decisions { get; set; } = [];
    public ICollection<HumanInputRequest> HumanInputRequests { get; set; } = [];
}

/// <summary>
/// One pass/fail record per execution attempt of a task. The schema is in
/// place from the initial migration so analytics queries don't need a
/// retrofit, but Api currently has no write path — rows are populated by the
/// Worker's agent-runtime pipeline (Phase 5+, planned with the Durable Task
/// orchestrator + IAgentRuntime implementation). Read-only queries against
/// existing rows are safe today.
/// </summary>
public class TaskAttempt : TaskScopedEntity
{
    public int AttemptNumber { get; set; }
    public AttemptStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public FailureTier? FailureTier { get; set; }
    public string? FailureReason { get; set; }
    public string? FeedbackProvided { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }
}

/// <summary>
/// Edge in the task dependency DAG: <see cref="TaskId"/> requires
/// <see cref="DependsOnTaskId"/> to be in a terminal state before it can run.
/// Like <see cref="TaskAttempt"/>, the schema exists from the initial
/// migration but no Api write path is wired — the workflow engine (Phase 5+)
/// owns dependency creation and resolution.
/// </summary>
public class TaskDependency
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
    public Guid DependsOnTaskId { get; set; }
    public AgenticTask DependsOnTask { get; set; } = null!;
}
