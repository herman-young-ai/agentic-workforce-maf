using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
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

    // The mutators below throw NotFoundException when the row has disappeared
    // between the handler's pre-load and the write (e.g. concurrent delete or
    // racing retract). Silent no-ops would mask the race and let callers
    // report success without doing the work (violates Principle 8).

    public async Task RetractAsync(Guid id, string retractedBy, string reason, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Learning", id);

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
        var old = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == oldId, ct)
            ?? throw new NotFoundException("Learning", oldId);

        db.ProjectLearnings.Add(replacement);
        old.Status         = LearningStatus.Superseded;
        old.SupersededById = replacement.Id;
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestPromotionAsync(Guid id, Guid requestedById, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Learning", id);

        learning.PromotionStatus       = PromotionStatus.PendingApproval;
        learning.PromotionRequestedAt  = DateTime.UtcNow;
        learning.PromotionRequestedById = requestedById;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApprovePromotionAsync(Guid id, Guid approvedById, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Learning", id);

        learning.PromotionStatus = PromotionStatus.Approved;
        learning.PromotedBy      = approvedById.ToString();
        learning.PromotedAt      = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectPromotionAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var learning = await db.ProjectLearnings.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Learning", id);

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

    public Task<PagedResult<ProjectLearning>> ListApprovedAcrossProjectsPagedAsync(
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.PromotionStatus == PromotionStatus.Approved)
            .OrderByDescending(l => l.PromotedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<IReadOnlyList<LearningMatch>> SearchByEmbeddingAsync(
        Guid projectId,
        float[] queryEmbedding,
        int limit,
        CancellationToken ct = default)
    {
        // Callers pass plain float[] (Domain stays free of Pgvector at the
        // interface surface). Conversion to Pgvector.Vector happens here so
        // the EF-translated pgvector `<=>` (CosineDistance) extension can
        // run. Cosine distance lives in [0, 2]; we map to a [0, 1] similarity
        // for the API.
        var pgQuery = new Vector(queryEmbedding);
        var rows = await db.ProjectLearnings
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId
                     && l.Status == LearningStatus.Active
                     && l.Embedding != null)
            .Select(l => new { Learning = l, Distance = l.Embedding!.CosineDistance(pgQuery) })
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
