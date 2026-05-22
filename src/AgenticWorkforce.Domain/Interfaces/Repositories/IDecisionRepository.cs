using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for project decisions. Decisions are never deleted (Principle
/// 13) — they are either Active, Superseded by another decision, or Reversed.
/// </summary>
public interface IDecisionRepository
{
    Task<ProjectDecision?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<ProjectDecision>> ListByProjectPagedAsync(
        Guid projectId,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<ProjectDecision> AddAsync(ProjectDecision decision, CancellationToken ct = default);
}
