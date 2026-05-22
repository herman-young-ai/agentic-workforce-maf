using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for ProjectAgent — the link between a project and an enabled
/// catalog agent, with project-specific customisations (display order, custom
/// constraints, enabled flag).
/// </summary>
public interface IProjectAgentRepository
{
    Task<ProjectAgent?> GetByIdAsync(Guid projectAgentId, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectAgent>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectAgent> AddAsync(ProjectAgent agent, CancellationToken ct = default);

    Task UpdateAsync(ProjectAgent agent, CancellationToken ct = default);

    Task<bool> RemoveAsync(Guid projectAgentId, CancellationToken ct = default);

    /// <summary>
    /// Adds every enabled catalog agent not already on the project as a new
    /// ProjectAgent in a single transaction. Returns the list of agents added;
    /// empty list means everything was already seeded.
    /// </summary>
    Task<IReadOnlyList<SeededProjectAgent>> SeedFromCatalogAsync(
        Guid projectId,
        CancellationToken ct = default);
}
