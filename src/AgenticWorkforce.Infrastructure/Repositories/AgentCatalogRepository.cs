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

    public async Task<IReadOnlyList<AgentCatalog>> ListAllAsync(CancellationToken ct = default)
        => await db.AgentCatalogs
            .AsNoTracking()
            .OrderBy(a => a.AgentName)
            .ToListAsync(ct);

    public async Task<AgentCatalog> AddAsync(AgentCatalog agent, CancellationToken ct = default)
    {
        db.AgentCatalogs.Add(agent);
        await db.SaveChangesAsync(ct);
        return agent;
    }

    public async Task UpdateAsync(AgentCatalog agent, CancellationToken ct = default)
    {
        db.AgentCatalogs.Update(agent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var agent = await db.AgentCatalogs.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return false;

        agent.Enabled = enabled;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
