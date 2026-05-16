namespace AgenticWorkforce.Domain.Entities;

public class Session : ProjectScopedEntity
{
    public string? Title { get; set; }
    public SessionType Type { get; set; } = SessionType.Chat;
    public bool IsActive { get; set; } = true;

    public Guid? UserId { get; set; }
    public PlatformUser? User { get; set; }

    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    public ICollection<SessionMessage> Messages { get; set; } = [];
}

public class SessionMessage : EntityBase
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public int TokenCount { get; set; }

    /// <summary>Tool calls made during this message (jsonb).</summary>
    public string? ToolCalls { get; set; }
}
