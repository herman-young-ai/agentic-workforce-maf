using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectContext : ProjectScopedEntity
{
    [Column(TypeName = "jsonb")]
    public string ContextData { get; set; } = "{}";

    public int ContextVersion { get; set; } = 1;
    public int SizeCharacters { get; set; }
    public int SizeTokens { get; set; }
    public string FormatVersion { get; set; } = "1.0";

    public ICollection<ContextChange> Changes { get; set; } = [];
}

public class ContextChange : ProjectScopedEntity
{
    public Guid ContextId { get; set; }
    public ProjectContext Context { get; set; } = null!;
    public int ContextVersion { get; set; }
    public ChangeType ChangeType { get; set; }
    public string Path { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public string? OldValue { get; set; }

    [Column(TypeName = "jsonb")]
    public string? NewValue { get; set; }

    public string? AgentName { get; set; }
    public Guid? TaskId { get; set; }
    public string Reason { get; set; } = null!;
}

public class ContextMilestone : ProjectScopedEntity
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = "";
    public int VersionSnapshot { get; set; }

    [Column(TypeName = "jsonb")]
    public string ContextSnapshot { get; set; } = "{}";

    public string CreatedBy { get; set; } = null!;
}
