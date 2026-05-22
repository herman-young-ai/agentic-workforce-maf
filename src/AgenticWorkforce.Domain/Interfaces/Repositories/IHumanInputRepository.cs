using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for human approval gates created during workflow runs.
/// </summary>
public interface IHumanInputRepository
{
    Task<HumanInputRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<HumanInputRequest>> ListPendingByProjectAsync(
        Guid projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Records the human decision in a single transaction. Enforces Principle
    /// 11 (Segregation of Duties): if <c>WorkflowRun.TriggeredById == responderId</c>
    /// the response is rejected and <see cref="RespondOutcome.Forbidden"/> is
    /// true. The repository sets <c>Decision</c>, <c>Response</c>, <c>ResponderId</c>,
    /// <c>ResolvedAt</c>, and transitions <c>Status</c> to
    /// <see cref="HumanInputRequestStatus.Completed"/>.
    /// </summary>
    Task<RespondOutcome> RespondAsync(
        Guid requestId,
        HumanDecisionType decision,
        string? response,
        Guid responderId,
        CancellationToken ct = default);
}
