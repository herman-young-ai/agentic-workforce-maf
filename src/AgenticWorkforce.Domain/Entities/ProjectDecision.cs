using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectDecision : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public string DecisionRef { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public string Decision { get; set; } = null!;
    public string Rationale { get; set; } = null!;
    public string MadeBy { get; set; } = null!;
    public Guid? WorkflowRunId { get; set; }
    public WorkflowRun? WorkflowRun { get; set; }
    public DecisionStatus Status { get; set; } = DecisionStatus.Active;
    public Guid? SupersededById { get; set; }
    public ProjectDecision? SupersededBy { get; set; }
}
