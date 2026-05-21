using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Query-only abstraction for the AgenticTask aggregate. Writes go through
/// <c>AppDbContext.Tasks</c> directly from vertical-slice handlers.
/// </summary>
public interface ITaskRepository
{
    Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<AgenticTask>> GetByProjectIdAsync(
        Guid projectId,
        TaskStatus? status = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgenticTask>> GetBoardAsync(Guid projectId, CancellationToken ct = default);
}
