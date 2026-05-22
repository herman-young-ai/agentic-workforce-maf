using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for milestone summaries — periodic roll-ups of project progress.
/// Milestones are immutable once created; no update method exposed.
/// </summary>
public interface IMilestoneRepository
{
    Task<MilestoneSummary?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<MilestoneSummary>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<MilestoneSummary> AddAsync(MilestoneSummary milestone, CancellationToken ct = default);
}
