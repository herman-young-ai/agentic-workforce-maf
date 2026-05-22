namespace AgenticWorkforce.Domain.Interfaces.Services;

public record AgentCostBreakdown(string AgentName, decimal CostUsd, int Calls);

public record ModelCostBreakdown(string Model, decimal CostUsd, int Calls);

public record CostSummary(
    decimal TotalUsd,
    IReadOnlyList<AgentCostBreakdown> ByAgent,
    IReadOnlyList<ModelCostBreakdown> ByModel);

public record CostTimelineEntry(DateTime Hour, decimal CostUsd, int Calls);

public record TokenEconomics(
    long TotalInput,
    long TotalOutput,
    long CacheRead,
    long CacheCreation,
    double CacheHitRate);

/// <summary>
/// Partition-aware aggregations over the range-partitioned <c>llm_calls</c>
/// table. All methods require explicit date bounds so PostgreSQL prunes
/// partitions — there is no default-to-all-time path because that would do
/// a full-history scan (Principle 19: bounded resource usage).
/// </summary>
public interface ICostQueryService
{
    Task<CostSummary> GetSummaryAsync(
        Guid projectId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAsync(
        Guid projectId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task<TokenEconomics> GetTokenEconomicsAsync(
        Guid projectId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="GetSummaryAsync"/> but cross-project (platform admin
    /// only — the calling endpoint is authorised separately).
    /// </summary>
    Task<CostSummary> GetSummaryAllProjectsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAllProjectsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
