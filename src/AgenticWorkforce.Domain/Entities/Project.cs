namespace AgenticWorkforce.Domain.Entities;

public class Project : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public ProjectPriority Priority { get; set; } = ProjectPriority.Medium;
    public SecurityClassification SecurityClassification { get; set; } = SecurityClassification.Internal;

    public Guid OwnerId { get; set; }
    public PlatformUser Owner { get; set; } = null!;

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Project-level settings (jsonb). Stored as opaque JSON.</summary>
    public string? Settings { get; set; }

    /// <summary>Arbitrary metadata (jsonb).</summary>
    public string? Metadata { get; set; }

    public DateTime? CompletedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // Navigation properties
    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<AgenticTask> Tasks { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
    public ICollection<ProjectAgent> Agents { get; set; } = [];
    public ICollection<ProjectDocument> Documents { get; set; } = [];
    public ICollection<ProjectLearning> Learnings { get; set; } = [];
    public ICollection<ProjectEvent> Events { get; set; } = [];
    public ICollection<WorkflowDefinition> Workflows { get; set; } = [];
    public ICollection<CostBudget> Budgets { get; set; } = [];
}

public class ProjectMember : EntityBase
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public PlatformUser User { get; set; } = null!;

    public ProjectRole Role { get; set; } = ProjectRole.Viewer;
}
