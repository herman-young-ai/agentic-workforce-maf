using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// PCD (Project Context Document) mutation service. Every mutation increments
/// <see cref="ProjectContext.ContextVersion"/> and records a
/// <see cref="ContextChange"/> row in the same transaction so the change
/// history is never out of sync with the current state.
/// </summary>
public interface IProjectContextService
{
    Task<ProjectContext> GetAsync(Guid projectId, CancellationToken ct = default);

    Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>
    /// Appends a principle to the PCD's <c>principles</c> array. Returns the
    /// new principle's stable id (used by the corresponding remove endpoint).
    /// </summary>
    Task<string> AddPrincipleAsync(
        Guid projectId,
        string principle,
        Guid addedById,
        CancellationToken ct = default);

    Task<string> AddGuardrailAsync(
        Guid projectId,
        string guardrail,
        Guid addedById,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the principle with the given id. Returns false if no such
    /// principle exists (idempotent delete).
    /// </summary>
    Task<bool> RemovePrincipleAsync(
        Guid projectId,
        string principleId,
        Guid removedById,
        CancellationToken ct = default);

    Task<bool> RemoveGuardrailAsync(
        Guid projectId,
        string guardrailId,
        Guid removedById,
        CancellationToken ct = default);
}
