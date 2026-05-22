using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the Project Context Document (PCD) and its change history.
/// Writes are versioned — every mutation increments
/// <see cref="ProjectContext.ContextVersion"/> and emits a
/// <see cref="ContextChange"/> row recording the path, old value, new value,
/// agent (if any), and reason.
/// </summary>
public interface IProjectContextRepository
{
    Task<ProjectContext?> GetAsync(Guid projectId, CancellationToken ct = default);

    Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectContext> EnsureCreatedAsync(Guid projectId, CancellationToken ct = default);
}
