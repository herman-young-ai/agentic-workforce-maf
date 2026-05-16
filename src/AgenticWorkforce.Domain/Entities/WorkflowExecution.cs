namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// A single execution of a workflow definition within a project.
/// Tracked by Durable Task SDK for durability.
/// </summary>
public class WorkflowExecution : ProjectScopedEntity
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Pending;

    /// <summary>Durable Task instance ID for correlation.</summary>
    public string? DurableTaskInstanceId { get; set; }

    /// <summary>Execution-level input (jsonb).</summary>
    public string? Input { get; set; }

    /// <summary>Execution-level output (jsonb).</summary>
    public string? Output { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public ICollection<WorkflowNodeExecution> NodeExecutions { get; set; } = [];
}

/// <summary>
/// Execution state for a single node within a workflow execution.
/// </summary>
public class WorkflowNodeExecution : EntityBase
{
    public Guid WorkflowExecutionId { get; set; }
    public WorkflowExecution WorkflowExecution { get; set; } = null!;

    public Guid WorkflowNodeId { get; set; }
    public WorkflowNode WorkflowNode { get; set; } = null!;

    public WorkflowNodeExecutionStatus Status { get; set; } = WorkflowNodeExecutionStatus.Pending;

    /// <summary>Associated task ID (for AgentTask nodes).</summary>
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }

    /// <summary>Node output (jsonb).</summary>
    public string? Output { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
