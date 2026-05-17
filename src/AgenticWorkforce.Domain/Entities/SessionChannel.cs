namespace AgenticWorkforce.Domain.Entities;

public class SessionChannel : EntityBase
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public string ChannelType { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public DateTime BoundAt { get; set; }
    public bool IsActive { get; set; } = true;
}
