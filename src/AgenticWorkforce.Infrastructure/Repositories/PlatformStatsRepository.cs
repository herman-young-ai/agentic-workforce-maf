using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Queries;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class PlatformStatsRepository(AppDbContext db) : IPlatformStatsRepository
{
    public async Task<PlatformOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var totalProjects  = await db.Projects.AsNoTracking().CountAsync(ct);
        var activeProjects = await db.Projects.AsNoTracking()
            .CountAsync(p => p.Status == ProjectStatus.Active, ct);
        var totalUsers     = await db.Users.AsNoTracking().CountAsync(ct);
        var activeUsers    = await db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct);
        var catalogSize    = await db.AgentCatalogs.AsNoTracking().CountAsync(a => a.Enabled, ct);

        return new PlatformOverview(totalProjects, activeProjects, totalUsers, activeUsers, catalogSize);
    }
}
