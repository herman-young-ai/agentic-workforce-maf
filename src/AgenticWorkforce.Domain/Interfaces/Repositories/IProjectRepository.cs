using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query-only abstraction for the Project aggregate. Writes go through
/// <c>AppDbContext.Projects.Add/Update</c> + <c>SaveChangesAsync</c> directly
/// from vertical-slice handlers — wrapping those one-liners adds no value and
/// fragments the unit of work across repositories.
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListByMemberAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}
