using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class Project : EntityBase
{
    public string Name { get; set; } = null!;
    public string Objective { get; set; } = null!;
    public string? Description { get; set; }
    public string? Brief { get; set; }
    public ProjectStatus Status { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal? BudgetCeilingUsd { get; set; }

    public string? Jurisdiction { get; set; }
    public string? TemplateName { get; set; }
    public ProjectTier Tier { get; set; }

    // Navigation
    public ProjectContext? Context { get; set; }
    public ICollection<ContextMilestone> Milestones { get; set; } = [];
    public ICollection<ProjectIntent> Intents { get; set; } = [];
    public ICollection<ProjectAgent> Agents { get; set; } = [];
    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<AgenticTask> Tasks { get; set; } = [];
    public ICollection<ProjectLearning> Learnings { get; set; } = [];
    public ICollection<ProjectDecision> Decisions { get; set; } = [];
    public ICollection<MilestoneSummary> MilestoneSummaries { get; set; } = [];
    public ICollection<ProjectArtifact> Artifacts { get; set; } = [];
    public ICollection<ProjectDocument> Documents { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
    public ICollection<ProjectEvent> Events { get; set; } = [];
    public ICollection<WorkflowDefinition> WorkflowDefinitions { get; set; } = [];
    public ICollection<WorkflowRun> WorkflowRuns { get; set; } = [];
    public ICollection<LlmCall> LlmCalls { get; set; } = [];
}

public class ProjectMember : ProjectScopedEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ProjectRole Role { get; set; }
}

public class ProjectAgent : ProjectScopedEntity
{
    public Guid AgentCatalogId { get; set; }
    public AgentCatalog AgentCatalog { get; set; } = null!;
    public AgentRole Role { get; set; }
    public string? UserPrompt { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }

    [Column(TypeName = "jsonb")]
    public string? CustomConstraints { get; set; }
}
