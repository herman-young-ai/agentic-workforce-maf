using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class IntentRepository(AppDbContext db) : IIntentRepository
{
    public Task<ProjectIntent?> GetCurrentAsync(Guid projectId, CancellationToken ct = default)
        => db.ProjectIntents
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProjectIntent>> GetHistoryAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.ProjectIntents
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<ProjectIntent> AddAsync(ProjectIntent intent, CancellationToken ct = default)
    {
        db.ProjectIntents.Add(intent);
        await db.SaveChangesAsync(ct);
        return intent;
    }
}
