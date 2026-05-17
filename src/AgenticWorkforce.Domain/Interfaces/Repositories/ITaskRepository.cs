using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

public interface ITaskRepository
{
    Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AgenticTask> CreateAsync(AgenticTask task, CancellationToken ct = default);
    Task<AgenticTask> UpdateAsync(AgenticTask task, CancellationToken ct = default);
}
