using System.ComponentModel.DataAnnotations;

namespace AgenticWorkforce.Domain.Entities;

public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // PostgreSQL xmin system column — optimistic concurrency.
    // Named RowVersion to avoid clashing with entity-level Version fields
    // (e.g. WorkflowDefinition.Version, PromptVersion.Version).
    [Timestamp]
    public uint RowVersion { get; set; }
}

public abstract class ProjectScopedEntity : EntityBase
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
}

public abstract class TaskScopedEntity : ProjectScopedEntity
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
}
