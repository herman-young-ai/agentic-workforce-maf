namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Base entity with UUID primary key and timestamps.
/// All timestamps are UTC DateTime (timezone-naive, stored as TIMESTAMPTZ in PostgreSQL).
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Base for entities scoped to a project. Cascading delete via Project FK.
/// </summary>
public abstract class ProjectScopedEntity : EntityBase
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
}

/// <summary>
/// Base for entities scoped to a task (which implies project scope through the task).
/// </summary>
public abstract class TaskScopedEntity : EntityBase
{
    public Guid TaskId { get; set; }
    public AgenticTask Task { get; set; } = null!;
}
