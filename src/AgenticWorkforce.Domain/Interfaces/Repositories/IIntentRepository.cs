using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for project intent — the canonical "what is this project for"
/// statement. Maintains a history chain via <c>RevisedFromId</c>.
/// </summary>
public interface IIntentRepository
{
    /// <summary>
    /// Returns the most recent intent for the project (the head of the
    /// revision chain), or null if the project has no intent yet.
    /// </summary>
    Task<ProjectIntent?> GetCurrentAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full revision chain newest-first.
    /// </summary>
    Task<IReadOnlyList<ProjectIntent>> GetHistoryAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectIntent> AddAsync(ProjectIntent intent, CancellationToken ct = default);
}
