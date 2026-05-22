using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class PromptVersionRepository(AppDbContext db) : IPromptVersionRepository
{
    public async Task<PromptVersion> AddAsync(PromptVersion version, CancellationToken ct = default)
    {
        db.PromptVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return version;
    }

    public async Task<IReadOnlyList<PromptVersion>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default)
        => await db.PromptVersions
            .AsNoTracking()
            .Where(p => p.EntityType == entityType && p.EntityId == entityId)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);

    public async Task<int> GetCurrentVersionAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default)
    {
        var current = await db.PromptVersions
            .Where(p => p.EntityType == entityType && p.EntityId == entityId)
            .Select(p => (int?)p.Version)
            .MaxAsync(ct);
        return current ?? 0;
    }
}
