namespace AgenticWorkforce.Domain.Entities;

public class PromptVersion : EntityBase
{
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string PromptType { get; set; } = null!;
    public string Content { get; set; } = null!;
    public int Version { get; set; }
    public string? ChangedBy { get; set; }
    public string? ChangeReason { get; set; }
}
