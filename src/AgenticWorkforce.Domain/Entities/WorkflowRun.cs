using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class WorkflowRun : ProjectScopedEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string WorkflowName { get; set; } = null!;
    public int WorkflowVersion { get; set; }
    public WorkflowRunStatus Status { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string? TriggerType { get; set; }

    // Human-readable provenance (e.g. "schedule-cron-0942", "agent: workflow.designer").
    // For typed user identity (used for SOD enforcement on human input approvals),
    // see TriggeredById.
    public string? TriggeredBy { get; set; }

    // FK to the User who triggered this run. Null when the trigger is a schedule
    // or agent (no owning human). Compared against the responder's user id to
    // enforce Principle 11 (Segregation of Duties) on HumanInputRequest.Respond.
    public Guid? TriggeredById { get; set; }
    public User? TriggeredByUser { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Context { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal TotalCostUsd { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal? BudgetUsd { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ErrorData { get; set; }

    public string? ResultSummary { get; set; }

    public ICollection<AgenticTask> Tasks { get; set; } = [];
    public ICollection<HumanInputRequest> HumanInputRequests { get; set; } = [];
}

public class WorkflowSchedule : ProjectScopedEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public string CronExpression { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
}

public class HumanInputRequest : ProjectScopedEntity
{
    public Guid WorkflowRunId { get; set; }
    public WorkflowRun WorkflowRun { get; set; } = null!;
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string PromptMessage { get; set; } = null!;
    public string? Channel { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Choices { get; set; }

    public HumanInputRequestStatus Status { get; set; }
    public HumanDecisionType? Decision { get; set; }
    public string? Response { get; set; }
    public Guid? ResponderId { get; set; }
    public User? Responder { get; set; }
    public DateTime? TimeoutAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
