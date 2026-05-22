using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class DecisionRepository(AppDbContext db) : IDecisionRepository
{
    public Task<ProjectDecision?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ProjectDecisions
            .Include(d => d.SupersededBy)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<PagedResult<ProjectDecision>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.ProjectDecisions
            .AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .OrderByDescending(d => d.CreatedAt)
            .ToPagedResultAsync(paging, ct);

    public async Task<ProjectDecision> AddAsync(ProjectDecision decision, CancellationToken ct = default)
    {
        db.ProjectDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
        return decision;
    }
}
