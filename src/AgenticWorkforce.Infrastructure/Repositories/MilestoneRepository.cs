using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class MilestoneRepository(AppDbContext db) : IMilestoneRepository
{
    public Task<MilestoneSummary?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.MilestoneSummaries.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<PagedResult<MilestoneSummary>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default)
        => db.MilestoneSummaries
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.PeriodEnd)
            .ToPagedResultAsync(paging, ct);

    public async Task<MilestoneSummary> AddAsync(MilestoneSummary milestone, CancellationToken ct = default)
    {
        db.MilestoneSummaries.Add(milestone);
        await db.SaveChangesAsync(ct);
        return milestone;
    }
}
