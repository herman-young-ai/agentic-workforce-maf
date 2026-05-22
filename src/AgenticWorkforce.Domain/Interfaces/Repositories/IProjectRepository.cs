using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the Project aggregate. All reads and writes flow through
/// this interface — Api handlers never inject EF Core types (rule DL-001,
/// Principle 4 Wrap the Core).
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<Project>> ListByMemberPagedAsync(
        Guid userId,
        ProjectStatus? statusFilter,
        PagedQuery paging,
        CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Creates the project AND the founder's membership (Owner role) in a single
    /// transaction. Returns the created project with members populated.
    /// </summary>
    Task<Project> CreateWithOwnerAsync(Project project, Guid ownerUserId, CancellationToken ct = default);

    Task UpdateAsync(Project project, CancellationToken ct = default);
}
