using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;
using Pgvector;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectLearning : ProjectScopedEntity
{
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }
    public LearningKind Kind { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? Recommendation { get; set; }

    [Column(TypeName = "numeric(3,2)")]
    public decimal Confidence { get; set; }

    public int OccurrenceCount { get; set; } = 1;

    [Column(TypeName = "jsonb")]
    public string? Evidence { get; set; }

    public string[] AgentNames { get; set; } = [];
    public string[] DomainTags { get; set; } = [];

    public LearningStatus Status { get; set; } = LearningStatus.Active;
    public string? RetractedBy { get; set; }
    public string? RetractedReason { get; set; }
    public Guid? SupersededById { get; set; }
    public ProjectLearning? SupersededBy { get; set; }
    public Guid? ContradictsId { get; set; }
    public ProjectLearning? Contradicts { get; set; }

    public PromotionStatus PromotionStatus { get; set; } = PromotionStatus.None;
    public DateTime? PromotionRequestedAt { get; set; }
    public Guid? PromotionRequestedById { get; set; }
    public User? PromotionRequestedBy { get; set; }
    public string? PromotionRejectedReason { get; set; }

    // Set on the Approved transition by PlatformAdmin (see Admin/Knowledge/ApprovePromotion).
    public string? PromotedBy { get; set; }
    public DateTime? PromotedAt { get; set; }

    public Vector? Embedding { get; set; }
    public string FormatVersion { get; set; } = "1.0";
}
