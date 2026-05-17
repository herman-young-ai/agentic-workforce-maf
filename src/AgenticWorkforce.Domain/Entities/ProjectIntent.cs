using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectIntent : ProjectScopedEntity
{
    public string Intent { get; set; } = null!;
    public string IntentSummary { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string Scope { get; set; } = "{}";

    public IntentSource Source { get; set; }
    public Guid? RevisedFromId { get; set; }
    public ProjectIntent? RevisedFrom { get; set; }
    public string Reason { get; set; } = null!;
    public string? AgentName { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
}
