namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Knowledge extracted during project execution. Follows five-type taxonomy:
/// PCD, Finding, Preference, Procedure, Fact.
/// Agent-extracted learnings default to Pending (human gate before activation).
/// </summary>
public class ProjectLearning : ProjectScopedEntity
{
    public LearningKind Kind { get; set; }
    public LearningStatus Status { get; set; } = LearningStatus.Pending;

    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>Agent that extracted this learning (nullable for human-entered).</summary>
    public string? ExtractedByAgent { get; set; }

    /// <summary>Task that produced this learning.</summary>
    public Guid? TaskId { get; set; }
    public AgenticTask? Task { get; set; }

    /// <summary>
    /// Embedding vector for semantic deduplication and retrieval.
    /// </summary>
    public float[]? Embedding { get; set; }

    /// <summary>Human who approved/retracted this learning.</summary>
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Retraction reason (Principle 13: Retract, don't delete).</summary>
    public string? RetractedReason { get; set; }
}

/// <summary>
/// Human decision recorded on a task (approval, rejection, escalation, override).
/// Immutable once created.
/// </summary>
public class Decision : TaskScopedEntity
{
    public DecisionType Type { get; set; }
    public string? Rationale { get; set; }

    public Guid DecidedBy { get; set; }
    public PlatformUser DecidedByUser { get; set; } = null!;

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Polished deliverable produced by a task (report, code, data, etc.).
/// Stored in Azure Blob Storage, referenced by URI.
/// </summary>
public class Artifact : TaskScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public ArtifactType Type { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Azure Blob Storage URI.</summary>
    public string BlobUri { get; set; } = string.Empty;

    /// <summary>SHA-256 content hash.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Artifact metadata (jsonb). Schema varies by type.</summary>
    public string? Metadata { get; set; }
}
