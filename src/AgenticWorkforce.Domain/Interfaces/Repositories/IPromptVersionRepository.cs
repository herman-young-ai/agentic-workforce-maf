using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the immutable PromptVersion log. Append-only by design —
/// updates and deletes are not exposed because prompt history is auditable.
/// </summary>
public interface IPromptVersionRepository
{
    Task<PromptVersion> AddAsync(PromptVersion version, CancellationToken ct = default);

    Task<IReadOnlyList<PromptVersion>> ListByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the highest <see cref="PromptVersion.Version"/> recorded for the
    /// entity, or 0 if no prior version exists. Used to compute the next
    /// version number when appending a new prompt.
    /// </summary>
    Task<int> GetCurrentVersionAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default);
}
