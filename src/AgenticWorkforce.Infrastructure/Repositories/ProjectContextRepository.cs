using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ProjectContextRepository(AppDbContext db) : IProjectContextRepository
{
    public Task<ProjectContext?> GetAsync(Guid projectId, CancellationToken ct = default)
        => db.ProjectContexts.FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

    public async Task<IReadOnlyList<ContextChange>> GetHistoryAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.ContextChanges
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<ProjectContext> EnsureCreatedAsync(Guid projectId, CancellationToken ct = default)
    {
        var existing = await db.ProjectContexts.FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (existing is not null) return existing;

        var fresh = new ProjectContext
        {
            ProjectId      = projectId,
            ContextData    = "{}",
            ContextVersion = 1,
            SizeCharacters = 2,
            SizeTokens     = 1,
            FormatVersion  = "1.0"
        };
        db.ProjectContexts.Add(fresh);
        await db.SaveChangesAsync(ct);
        return fresh;
    }
}
