using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the global AgentCatalog — the platform-wide registry of
/// available agents. Writes are reserved for platform admin tooling (Phase 4
/// §4.17); Phase 3.5 only needs read methods.
/// </summary>
public interface IAgentCatalogRepository
{
    Task<AgentCatalog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AgentCatalog?> GetByNameAsync(string agentName, CancellationToken ct = default);

    Task<IReadOnlyList<AgentCatalog>> ListEnabledAsync(CancellationToken ct = default);
}
