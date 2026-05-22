using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class LearningRepository(AppDbContext db) : ILearningRepository
{
    public Task<ProjectLearning?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<PagedResult<ProjectLearning>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId && l.Status == LearningStatus.Active)
            .OrderByDescending(l => l.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<ProjectLearning> AddAsync(ProjectLearning learning, CancellationToken ct = default)
    {
        db.ProjectLearnings.Add(learning);
        await db.SaveChangesAsync(ct);
        return learning;
    }

    public async Task UpdateAsync(ProjectLearning learning, CancellationToken ct = default)
    {
        db.ProjectLearnings.Update(learning);
        await db.SaveChangesAsync(ct);
    }

    public async Task RetractAsync(Guid id, string retractedBy, string reason, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (learning is null) return;

        learning.Status          = LearningStatus.Retracted;
        learning.RetractedBy     = retractedBy;
        learning.RetractedReason = reason;
        await db.SaveChangesAsync(ct);
    }

    public async Task SupersedeAsync(
        Guid oldId,
        ProjectLearning replacement,
        CancellationToken ct = default)
    {
        var old = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == oldId, ct);
        if (old is null) return;

        db.ProjectLearnings.Add(replacement);
        old.Status         = LearningStatus.Superseded;
        old.SupersededById = replacement.Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestPromotionAsync(Guid id, Guid requestedById, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (learning is null) return;

        learning.PromotionStatus       = PromotionStatus.PendingApproval;
        learning.PromotionRequestedAt  = DateTime.UtcNow;
        learning.PromotionRequestedById = requestedById;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApprovePromotionAsync(Guid id, Guid approvedById, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (learning is null) return;

        learning.PromotionStatus = PromotionStatus.Approved;
        learning.PromotedBy      = approvedById.ToString();
        learning.PromotedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectPromotionAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (learning is null) return;

        learning.PromotionStatus        = PromotionStatus.Rejected;
        learning.PromotionRejectedReason = reason;
        await db.SaveChangesAsync(ct);
    }

    public Task<PagedResult<ProjectLearning>> ListPendingPromotionsPagedAsync(
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.PromotionStatus == PromotionStatus.PendingApproval)
            .OrderBy(l => l.PromotionRequestedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<IReadOnlyList<LearningMatch>> SearchByEmbeddingAsync(
        Guid projectId,
        Vector queryEmbedding,
        int limit,
        CancellationToken ct = default)
    {
        // Cosine distance via pgvector's `<=>` operator (Pgvector.EFCore extension).
        // Convert distance in [0, 2] to a similarity score in [0, 1] for the API.
        var rows = await db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId
                     && l.Status == LearningStatus.Active
                     && l.Embedding != null)
            .Select(l => new { Learning = l, Distance = l.Embedding!.CosineDistance(queryEmbedding) })
            .OrderBy(r => r.Distance)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(r => new LearningMatch(r.Learning, 1.0 - r.Distance / 2.0))
            .ToList();
    }

    public async Task<IReadOnlyList<LearningMatch>> FindSimilarAsync(
        Guid learningId,
        int limit,
        CancellationToken ct = default)
    {
        var seed = await db.ProjectLearnings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == learningId, ct);

        if (seed?.Embedding is null) return [];

        var rows = await db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.ProjectId == seed.ProjectId
                     && l.Id != learningId
                     && l.Status == LearningStatus.Active
                     && l.Embedding != null)
            .Select(l => new { Learning = l, Distance = l.Embedding!.CosineDistance(seed.Embedding) })
            .OrderBy(r => r.Distance)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(r => new LearningMatch(r.Learning, 1.0 - r.Distance / 2.0))
            .ToList();
    }
}
