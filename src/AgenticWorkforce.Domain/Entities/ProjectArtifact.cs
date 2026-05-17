using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectArtifact : TaskScopedEntity
{
    public string? AgentName { get; set; }
    public ArtifactType ArtifactType { get; set; }
    public string Title { get; set; } = null!;
    public ContentFormat ContentFormat { get; set; }
    public string? ContentText { get; set; }
    public string? StorageUrl { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public string? Language { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Metadata { get; set; }

    public string FormatVersion { get; set; } = "1.0";
}
