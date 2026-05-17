using System.ComponentModel.DataAnnotations.Schema;

namespace AgenticWorkforce.Domain.Entities;

public class LlmCall : EntityBase
{
    public Guid? SessionId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? TaskId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentRole { get; set; }
    public string Model { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }

    public int LatencyMs { get; set; }
    public string? RequestId { get; set; }
    public int ToolCount { get; set; }
}
