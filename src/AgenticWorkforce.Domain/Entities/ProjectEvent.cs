namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Domain-level event log for a project. Feeds the project console in the UI
/// and provides a queryable timeline of project activity.
/// Partitioned by month in PostgreSQL for query performance.
/// </summary>
public class ProjectEvent : ProjectScopedEntity
{
    public string EventType { get; set; } = string.Empty;
    public EventSeverity Severity { get; set; } = EventSeverity.Info;
    public string Message { get; set; } = string.Empty;

    /// <summary>Event payload (jsonb). Schema varies by EventType.</summary>
    public string? Data { get; set; }

    /// <summary>Agent that emitted this event (nullable for system events).</summary>
    public string? AgentName { get; set; }

    /// <summary>Associated task (nullable).</summary>
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }

    /// <summary>User who triggered this event (nullable for agent events).</summary>
    public Guid? UserId { get; set; }
}
