using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Write-side persistence for <see cref="LlmCall"/> rows. The read-side aggregator
/// is <see cref="Services.ICostQueryService"/> — both target the partitioned
/// <c>llm_calls</c> table but the write path is hot (one row per provider call)
/// and uses bulk insert to amortise round-trips.
/// </summary>
public interface ILlmCallRepository
{
    Task AddBatchAsync(IReadOnlyList<LlmCall> calls, CancellationToken ct = default);
}
