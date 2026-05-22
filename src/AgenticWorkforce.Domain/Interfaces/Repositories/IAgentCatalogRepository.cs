using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the global AgentCatalog — the platform-wide registry of
/// available agents. Read methods serve both project workflows (enabled
/// agents only) and admin tooling (all entries).
/// </summary>
public interface IAgentCatalogRepository
{
    Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AgentCatalog?> GetByNameAsync(string agentName, CancellationToken ct = default);

    Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Admin-only: returns every catalog row regardless of Enabled or
    /// Visibility. Caller must be authorised at the PlatformAdmin policy
    /// level before invoking.
    /// </summary>
    Task<IReadOnlyList<AgentCatalog>> ListAllAsync(CancellationToken ct = default);

    Task<AgentCatalog> AddAsync(AgentCatalog agent, CancellationToken ct = default);

    Task UpdateAsync(AgentCatalog agent, CancellationToken ct = default);

    Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
}
