using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for uploaded reference documents and their text-extracted
/// chunks. Documents are blob-stored (via <see cref="Services.IDocumentStore"/>),
/// metadata + chunks live in PostgreSQL.
/// </summary>
public interface IDocumentRepository
{
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProjectDocument>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<ProjectDocument> AddAsync(ProjectDocument document, CancellationToken ct = default);

    Task<bool> RetractAsync(Guid id, string retractedBy, CancellationToken ct = default);

    /// <summary>
    /// Vector similarity search across the project's chunks. Caller must
    /// have already obtained a query embedding via <c>IEmbeddingService</c>.
    /// Excludes chunks from retracted documents.
    /// </summary>
    Task<IReadOnlyList<DocumentChunkMatch>> SearchChunksAsync(
        Guid projectId,
        float[] queryEmbedding,
        int limit,
        CancellationToken ct = default);
}
