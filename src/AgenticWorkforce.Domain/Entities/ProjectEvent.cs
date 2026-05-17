using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectEvent : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string EventType { get; set; } = null!;
    public string? Source { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Data { get; set; }

    public EventSeverity Severity { get; set; }
}
