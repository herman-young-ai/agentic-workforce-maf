using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class AgentCatalogRepository(AppDbContext db) : IAgentCatalogRepository
{
    public Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.AgentCatalogs.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<AgentCatalog?> GetByNameAsync(string agentName, CancellationToken ct = default)
        => db.AgentCatalogs.FirstOrDefaultAsync(a => a.AgentName == agentName, ct);

    public async Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default)
        => await db.AgentCatalogs
            .AsNoTracking()
            .Where(a => a.Enabled)
            .OrderBy(a => a.AgentName)
            .ToListAsync(ct);
}
