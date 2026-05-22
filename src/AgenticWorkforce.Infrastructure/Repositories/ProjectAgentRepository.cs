using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Repositories;

internal sealed class ProjectAgentRepository(AppDbContext db) : IProjectAgentRepository
{
    public Task<ProjectAgent?> GetByIdAsync(Guid projectAgentId, CancellationToken ct = default)
        => db.ProjectAgents
            .Include(a => a.AgentCatalog)
            .FirstOrDefaultAsync(a => a.Id == projectAgentId, ct);

    public async Task<IReadOnlyList<ProjectAgent>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
        => await db.ProjectAgents
            .AsNoTracking()
            .Include(a => a.AgentCatalog)
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync(ct);

    public async Task<ProjectAgent> AddAsync(ProjectAgent agent, CancellationToken ct = default)
    {
        db.ProjectAgents.Add(agent);
        await db.SaveChangesAsync(ct);
        return agent;
    }

    public async Task UpdateAsync(ProjectAgent agent, CancellationToken ct = default)
    {
        db.ProjectAgents.Update(agent);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid projectAgentId, CancellationToken ct = default)
    {
        var agent = await db.ProjectAgents.FirstOrDefaultAsync(a => a.Id == projectAgentId, ct);
        if (agent is null)
            return false;

        db.ProjectAgents.Remove(agent);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SeededProjectAgent>> SeedFromCatalogAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var existingCatalogIds = await db.ProjectAgents
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.AgentCatalogId)
            .ToListAsync(ct);

        var toAdd = await db.AgentCatalogs
            .AsNoTracking()
            .Where(c => c.Enabled && !existingCatalogIds.Contains(c.Id))
            .OrderBy(c => c.AgentName)
            .ToListAsync(ct);

        if (toAdd.Count == 0)
            return [];

        var maxDisplayOrder = await db.ProjectAgents
            .Where(a => a.ProjectId == projectId)
            .Select(a => (int?)a.DisplayOrder)
            .MaxAsync(ct) ?? 0;

        var added = new List<SeededProjectAgent>();
        foreach (var catalog in toAdd)
        {
            var agent = new ProjectAgent
            {
                ProjectId      = projectId,
                AgentCatalogId = catalog.Id,
                Role           = AgentRole.Specialist,
                Enabled        = true,
                DisplayOrder   = ++maxDisplayOrder
            };
            db.ProjectAgents.Add(agent);
            added.Add(new SeededProjectAgent(agent.Id, catalog.Id, catalog.AgentName));
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
