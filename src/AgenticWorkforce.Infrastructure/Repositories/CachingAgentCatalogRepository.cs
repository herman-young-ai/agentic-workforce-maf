using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace AgenticWorkforce.Infrastructure.Repositories;

/// <summary>
/// 5-minute in-memory cache decorator over <see cref="AgentCatalogRepository"/>.
/// Write paths evict matching keys before delegating so callers never read
/// stale rows immediately after an update.
/// </summary>
internal sealed class CachingAgentCatalogRepository(
    AgentCatalogRepository inner,
    IMemoryCache cache) : IAgentCatalogRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static string IdKey(Guid id) => $"agent:id:{id}";
    private static string NameKey(string n) => $"agent:name:{n}";

    public Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => cache.GetOrCreateAsync(IdKey(id), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return inner.GetByIdAsync(id, ct);
        })!;

    public Task<AgentCatalog?> GetByNameAsync(string agentName, CancellationToken ct = default)
        => cache.GetOrCreateAsync(NameKey(agentName), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return inner.GetByNameAsync(agentName, ct);
        })!;

    public Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default)
        => inner.ListEnabledAsync(ct);

    public Task<IReadOnlyList<AgentCatalog>> ListAllAsync(CancellationToken ct = default)
        => inner.ListAllAsync(ct);

    public async Task<AgentCatalog> AddAsync(AgentCatalog agent, CancellationToken ct = default)
    {
        var added = await inner.AddAsync(agent, ct);
        cache.Remove(NameKey(added.AgentName));
        cache.Remove(IdKey(added.Id));
        return added;
    }

    public async Task UpdateAsync(AgentCatalog agent, CancellationToken ct = default)
    {
        await inner.UpdateAsync(agent, ct);
        cache.Remove(NameKey(agent.AgentName));
        cache.Remove(IdKey(agent.Id));
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        // Look up the agent first so we know which name key to evict. The kill-switch
        // path (disable an agent) must take effect immediately — a stale name->row read
        // for up to 5 minutes after disable would let agents run that an admin just halted
        // (Principle 17 — Human Authority). The extra DB round trip is acceptable for the
        // rare enable/disable case.
        var existing = await inner.GetByIdAsync(id, ct);
        var result = await inner.SetEnabledAsync(id, enabled, ct);
        cache.Remove(IdKey(id));
        if (existing is not null) cache.Remove(NameKey(existing.AgentName));
        return result;
    }
}
