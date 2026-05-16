using System.Numerics;

namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Uploaded document attached to a project. Content extracted into chunks
/// for RAG retrieval via pgvector.
/// </summary>
public class ProjectDocument : ProjectScopedEntity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Azure Blob Storage URI for the original file.</summary>
    public string BlobUri { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the file content for deduplication.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public Guid UploadedBy { get; set; }
    public int ChunkCount { get; set; }
    public bool IsProcessed { get; set; }

    // Navigation properties
    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}

/// <summary>
/// A chunk of extracted text from a document, with its embedding vector
/// for semantic search via pgvector.
/// </summary>
public class DocumentChunk : EntityBase
{
    public Guid DocumentId { get; set; }
    public ProjectDocument Document { get; set; } = null!;

    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }

    /// <summary>
    /// Embedding vector (1536 dimensions for text-embedding-3-small).
    /// Stored as pgvector vector(1536) in PostgreSQL.
    /// Represented as float[] in C# — mapped by Pgvector.EntityFrameworkCore.
    /// </summary>
    public float[]? Embedding { get; set; }
}
