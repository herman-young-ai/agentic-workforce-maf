using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class CatalogQueryRepository(AppDbContext db) : ICatalogQueryRepository
{
    public Task<PagedResult<AgentCatalog>> ListVisibleAsync(
        bool isPlatformAdmin,
        PagedQuery paging,
        CancellationToken ct = default)
    {
        var query = db.AgentCatalogs.AsNoTracking().Where(a => a.Enabled);

        // Non-admin: only Public. Admin browse can additionally see Internal
        // (but Private remains hidden — Private is for catalog-staging).
        query = isPlatformAdmin
            ? query.Where(a => a.Visibility != AgentVisibility.Private)
            : query.Where(a => a.Visibility == AgentVisibility.Public);

        return query
            .OrderBy(a => a.AgentName)
            .ToPagedResultAsync(paging, ct);
    }

    public Task<AgentCatalog?> GetByIdVisibleAsync(
        Guid id,
        bool isPlatformAdmin,
        CancellationToken ct = default)
    {
        var query = db.AgentCatalogs.AsNoTracking().Where(a => a.Id == id && a.Enabled);

        query = isPlatformAdmin
            ? query.Where(a => a.Visibility != AgentVisibility.Private)
            : query.Where(a => a.Visibility == AgentVisibility.Public);

        return query.FirstOrDefaultAsync(ct);
    }
}
