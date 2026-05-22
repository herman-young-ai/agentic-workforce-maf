using AgenticWorkforce.Domain.Enums;
using Pgvector;

namespace AgenticWorkforce.Domain.Entities;

public class ProjectDocument : ProjectScopedEntity
{
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string StorageUrl { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? ExtractedText { get; set; }
    public string? ExtractedTextUrl { get; set; }
    public int? PageCount { get; set; }
    public ExtractionStatus ExtractionStatus { get; set; }
    public string? ExtractionError { get; set; }
    public DocumentType DocumentType { get; set; }
    public string? Description { get; set; }
    public string[] Tags { get; set; } = [];
    public bool EmbeddingsGenerated { get; set; }
    public int ChunkCount { get; set; }
    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;

    // Soft retraction (Principle 13: retract, never hard-delete).
    public DateTime? RetractedAt { get; set; }
    public string? RetractedBy { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}

public class DocumentChunk : ProjectScopedEntity
{
    public Guid DocumentId { get; set; }
    public ProjectDocument Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = null!;
    public Vector? Embedding { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
}
