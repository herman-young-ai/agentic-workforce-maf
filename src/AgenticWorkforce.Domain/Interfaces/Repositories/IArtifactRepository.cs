using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for project artifacts — the polished deliverables that
/// stakeholders care about (reports, generated documents, code).
/// </summary>
public interface IArtifactRepository
{
    Task<ProjectArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProjectArtifact>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<ArtifactContent?> GetContentAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Soft-retract an artifact (Principle 13: never truly delete a
    /// deliverable). Returns false if the artifact does not exist.
    /// </summary>
    Task<bool> RetractAsync(Guid id, string retractedBy, CancellationToken ct = default);
}
