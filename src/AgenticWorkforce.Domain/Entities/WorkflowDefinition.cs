namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Editable directed graph stored as JSON. Defines the workflow structure
/// with nodes and edges. Can be project-scoped or platform-wide.
/// </summary>
public class WorkflowDefinition : EntityBase
{
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SemanticVersion { get; set; } = "1.0.0";
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<WorkflowNode> Nodes { get; set; } = [];
    public ICollection<WorkflowEdge> Edges { get; set; } = [];
    public ICollection<WorkflowExecution> Executions { get; set; } = [];
}

public class WorkflowNode : EntityBase
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public WorkflowNodeType NodeType { get; set; }

    /// <summary>Node configuration (jsonb). Schema depends on NodeType.</summary>
    public string? Config { get; set; }

    /// <summary>Visual position for the React Flow editor (jsonb).</summary>
    public string? Position { get; set; }
}

public class WorkflowEdge : EntityBase
{
    public Guid WorkflowDefinitionId { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public Guid SourceNodeId { get; set; }
    public WorkflowNode SourceNode { get; set; } = null!;

    public Guid TargetNodeId { get; set; }
    public WorkflowNode TargetNode { get; set; } = null!;

    /// <summary>Condition expression for conditional edges (nullable).</summary>
    public string? Condition { get; set; }

    public string? Label { get; set; }
}
