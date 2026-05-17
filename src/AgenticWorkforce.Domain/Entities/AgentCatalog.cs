using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class AgentCatalog : EntityBase
{
    public string AgentName { get; set; } = null!;
    public string? AgentType { get; set; }
    public string? AgentVersion { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ModelConfig { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Tools { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Scope { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Interface { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Constraints { get; set; }

    public string[] Keywords { get; set; } = [];

    [Column(TypeName = "jsonb")]
    public string? ThinkingBudget { get; set; }

    public bool Enabled { get; set; } = true;
    public bool ChatEnabled { get; set; }
    public AgentVisibility Visibility { get; set; }
    public string? Engine { get; set; }
    public int? MaxInputLength { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal? MaxBudgetUsd { get; set; }

    public bool ProducesArtifact { get; set; }
    public string? ArtifactType { get; set; }

    public ICollection<ProjectAgent> ProjectAgents { get; set; } = [];
}
