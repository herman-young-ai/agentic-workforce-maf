using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Project> CreateAsync(Project project, CancellationToken ct = default);
    Task<Project> UpdateAsync(Project project, CancellationToken ct = default);
}

public interface ITaskRepository
{
    Task<AgenticTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AgenticTask>> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default);
    Task<AgenticTask> CreateAsync(AgenticTask task, CancellationToken ct = default);
    Task<AgenticTask> UpdateAsync(AgenticTask task, CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Session> CreateAsync(Session session, CancellationToken ct = default);
    Task<Session> UpdateAsync(Session session, CancellationToken ct = default);
}
