using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class Session : ProjectScopedEntity
{
    public SessionStatus Status { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string? AgentName { get; set; }
    public string? Goal { get; set; }
    public string? RollingSummary { get; set; }
    public int? RollingSummaryAnchor { get; set; }
    public int RollingSummaryVersion { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal TotalCostUsd { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal? CostBudgetUsd { get; set; }

    public DateTime? LastActivityAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public ICollection<SessionMessage> Messages { get; set; } = [];
    public ICollection<SessionChannel> Channels { get; set; } = [];
}

public class SessionMessage : EntityBase
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public MessageRole Role { get; set; }
    public string? Content { get; set; }
    public string? SenderId { get; set; }
    public string? Model { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    [Column(TypeName = "numeric(12,6)")]
    public decimal CostUsd { get; set; }

    public string? Thinking { get; set; }
    public string? ToolName { get; set; }
    public string? ToolCallId { get; set; }
    public string? Status { get; set; }
}
