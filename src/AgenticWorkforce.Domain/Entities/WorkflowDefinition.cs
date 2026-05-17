using System.ComponentModel.DataAnnotations.Schema;

namespace AgenticWorkforce.Domain.Entities;

public class WorkflowDefinition : EntityBase
{
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;

    [Column(TypeName = "jsonb")]
    public string Nodes { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string Edges { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string? CanvasState { get; set; }

    public string? DesignedBy { get; set; }
    public string? DesignedByAgent { get; set; }
    public DateTime? LockedAt { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    public ICollection<WorkflowSchedule> Schedules { get; set; } = [];
}
