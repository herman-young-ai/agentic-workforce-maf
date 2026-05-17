namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Enforces and tracks USD budget against a project (and optionally a session).
/// Budget ceilings live on Project.BudgetCeilingUsd and Session.CostBudgetUsd.
/// </summary>
public interface IBudgetService
{
    Task<bool> CanSpendAsync(
        Guid projectId,
        Guid? sessionId,
        decimal estimatedCostUsd,
        CancellationToken ct = default);

    Task RecordSpendAsync(
        Guid projectId,
        Guid? sessionId,
        Guid? taskId,
        decimal costUsd,
        CancellationToken ct = default);

    Task<BudgetStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default);
}

public record BudgetStatus(
    decimal CeilingUsd,
    decimal UsedUsd,
    decimal RemainingUsd,
    bool IsExhausted);
