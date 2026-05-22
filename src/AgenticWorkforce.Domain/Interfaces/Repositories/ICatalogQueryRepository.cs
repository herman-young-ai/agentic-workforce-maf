using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Restricted read view of the agent catalog for browse endpoints. Filters
/// out non-public visibility for non-admin readers — admin tooling uses
/// <see cref="IAgentCatalogRepository"/> directly to see everything.
/// </summary>
public interface ICatalogQueryRepository
{
    /// <summary>
    /// Lists enabled catalog entries whose visibility is <see cref="AgentVisibility.Public"/>
    /// (or <see cref="AgentVisibility.Internal"/> when the caller is a platform
    /// admin). Ordered by AgentName.
    /// </summary>
    Task<PagedResult<AgentCatalog>> ListVisibleAsync(
        bool isPlatformAdmin,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<AgentCatalog?> GetByIdVisibleAsync(
        Guid id,
        bool isPlatformAdmin,
        CancellationToken ct = default);
}
