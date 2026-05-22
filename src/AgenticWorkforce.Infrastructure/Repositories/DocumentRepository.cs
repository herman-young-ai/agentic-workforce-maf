using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class DocumentRepository(AppDbContext db) : IDocumentRepository
{
    public Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ProjectDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<PagedResult<ProjectDocument>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectDocuments
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.RetractedAt == null)
            .OrderByDescending(d => d.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<ProjectDocument> AddAsync(ProjectDocument document, CancellationToken ct = default)
    {
        db.ProjectDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }

    public async Task<bool> RetractAsync(Guid id, string retractedBy, CancellationToken ct = default)
    {
        var doc = await db.ProjectDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return false;
        if (doc.RetractedAt is not null) return true;

        doc.RetractedAt = DateTime.UtcNow;
        doc.RetractedBy = retractedBy;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DocumentChunkMatch>> SearchChunksAsync(
        Guid projectId,
        Vector queryEmbedding,
        int limit,
        CancellationToken ct = default)
    {
        // Join chunks to documents so we can exclude retracted ones. Score is
        // 1 - cosine_distance/2 to map the [0, 2] distance to [0, 1] similarity.
        var rows = await (
            from chunk in db.DocumentChunks.AsNoTracking()
            join doc in db.ProjectDocuments.AsNoTracking() on chunk.DocumentId equals doc.Id
            where doc.ProjectId == projectId
               && doc.RetractedAt == null
               && chunk.Embedding != null
            select new { Chunk = chunk, Distance = chunk.Embedding!.CosineDistance(queryEmbedding) }
        )
            .OrderBy(r => r.Distance)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(r => new DocumentChunkMatch(r.Chunk, 1.0 - r.Distance / 2.0))
            .ToList();
    }
}
