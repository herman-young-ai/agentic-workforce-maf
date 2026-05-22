using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Infrastructure.Services;

public sealed class CostQueryOptions
{
    /// <summary>
    /// Maximum range a single query may span. Defaults to 365 days. Queries
    /// outside this bound throw <see cref="ValidationException"/> so callers
    /// cannot accidentally scan the entire history.
    /// </summary>
    public int MaxRangeDays { get; set; } = 365;
}

/// <summary>
/// Partition-aware aggregations over the RANGE-partitioned <c>llm_calls</c>
/// table. Every query filters on <c>(project_id, created_at)</c> so PostgreSQL
/// prunes partitions; the composite index of the same shape (created in the
/// CreatePartitionedTables migration) backs all reads.
/// </summary>
internal sealed class CostQueryService(
    AppDbContext db,
    IOptions<CostQueryOptions> options) : ICostQueryService
{
    private readonly int _maxRangeDays = options.Value.MaxRangeDays;

    public async Task<CostSummary> GetSummaryAsync(
        Guid projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        Validate(from, to);

        var rows = await db.LlmCalls
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.CreatedAt >= from && c.CreatedAt <= to)
            .ToListAsync(ct);

        return BuildSummary(rows.Select(r => (r.AgentName, r.Model, r.CostUsd)).ToList());
    }

    public async Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAsync(
        Guid projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        Validate(from, to);

        // Group by date_trunc('hour', created_at) — pgvector-independent.
        var rows = await db.LlmCalls
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.CreatedAt >= from && c.CreatedAt <= to)
            .GroupBy(c => new DateTime(c.CreatedAt.Year, c.CreatedAt.Month, c.CreatedAt.Day, c.CreatedAt.Hour, 0, 0, DateTimeKind.Utc))
            .Select(g => new CostTimelineEntry(g.Key, g.Sum(x => x.CostUsd), g.Count()))
            .OrderBy(e => e.Hour)
            .ToListAsync(ct);

        return rows;
    }

    public async Task<TokenEconomics> GetTokenEconomicsAsync(
        Guid projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        Validate(from, to);

        var sums = await db.LlmCalls
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.CreatedAt >= from && c.CreatedAt <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalInput     = g.Sum(x => x.InputTokens),
                TotalOutput    = g.Sum(x => x.OutputTokens),
                CacheRead      = g.Sum(x => x.CacheReadTokens),
                CacheCreation  = g.Sum(x => x.CacheCreationTokens)
            })
            .FirstOrDefaultAsync(ct);

        if (sums is null)
            return new TokenEconomics(0, 0, 0, 0, 0);

        var billableInput = sums.TotalInput + sums.CacheRead + sums.CacheCreation;
        var cacheHitRate  = billableInput == 0 ? 0.0 : (double)sums.CacheRead / billableInput;

        return new TokenEconomics(
            sums.TotalInput, sums.TotalOutput, sums.CacheRead, sums.CacheCreation, cacheHitRate);
    }

    public async Task<CostSummary> GetSummaryAllProjectsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        Validate(from, to);

        var rows = await db.LlmCalls
            .AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
            .ToListAsync(ct);

        return BuildSummary(rows.Select(r => (r.AgentName, r.Model, r.CostUsd)).ToList());
    }

    public async Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAllProjectsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        Validate(from, to);

        var rows = await db.LlmCalls
            .AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.CreatedAt <= to)
            .GroupBy(c => new DateTime(c.CreatedAt.Year, c.CreatedAt.Month, c.CreatedAt.Day, c.CreatedAt.Hour, 0, 0, DateTimeKind.Utc))
            .Select(g => new CostTimelineEntry(g.Key, g.Sum(x => x.CostUsd), g.Count()))
            .OrderBy(e => e.Hour)
            .ToListAsync(ct);

        return rows;
    }

    private void Validate(DateTime from, DateTime to)
    {
        if (to <= from)
            throw new ValidationException("'to' must be after 'from'.");
        if ((to - from).TotalDays > _maxRangeDays)
            throw new ValidationException(
                $"Date range exceeds the maximum of {_maxRangeDays} days. Narrow the bounds and retry.");
    }

    private static CostSummary BuildSummary(IReadOnlyList<(string? AgentName, string Model, decimal CostUsd)> rows)
    {
        var byAgent = rows
            .GroupBy(r => r.AgentName ?? "(unattributed)")
            .Select(g => new AgentCostBreakdown(g.Key, g.Sum(x => x.CostUsd), g.Count()))
            .OrderByDescending(b => b.CostUsd)
            .ToList();

        var byModel = rows
            .GroupBy(r => r.Model)
            .Select(g => new ModelCostBreakdown(g.Key, g.Sum(x => x.CostUsd), g.Count()))
            .OrderByDescending(b => b.CostUsd)
            .ToList();

        return new CostSummary(rows.Sum(r => r.CostUsd), byAgent, byModel);
    }
}
