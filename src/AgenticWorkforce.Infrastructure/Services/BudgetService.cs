using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Budget tracker. Source of truth for spend is the sum of <c>LlmCall.CostUsd</c>
/// rows for the project — single read source (Principle 16). The partitioned
/// table makes per-project sum cheap at current scale; if it becomes a hotspot
/// introduce a materialised running total in a follow-up phase rather than
/// speculatively now.
/// </summary>
internal sealed class BudgetService(
    AppDbContext db,
    IOptions<BudgetServiceOptions> options,
    ILogger<BudgetService> logger) : IBudgetService
{
    private readonly decimal _warnThreshold = options.Value.WarningThreshold;

    public async Task<bool> CanSpendAsync(
        Guid projectId, Guid? sessionId, decimal estimatedCostUsd, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(projectId, ct);
        if (status.CeilingUsd <= 0) return true;     // No ceiling configured -> unbounded (explicit project setting required to cap)
        if (status.IsExhausted) return false;
        return status.RemainingUsd >= estimatedCostUsd;
    }

    public async Task RecordSpendAsync(
        Guid projectId, Guid? sessionId, Guid? taskId, decimal costUsd, CancellationToken ct = default)
    {
        // Spend is the sum of LlmCall rows; CostTrackingChatClient has already written the row.
        // This hook exists so we can (a) emit a budget-warning at 80% and (b) provide a single
        // future seam for an explicit counter table if sum-over-LlmCalls becomes a hotspot.
        var status = await GetStatusAsync(projectId, ct);
        if (status.CeilingUsd > 0 && status.UsedUsd / status.CeilingUsd >= _warnThreshold)
        {
            logger.LogWarning(
                "Project {ProjectId} budget at {Used:F2}/{Ceiling:F2} USD ({Pct:P0}); spend just recorded {SpendUsd:F4}.",
                projectId, status.UsedUsd, status.CeilingUsd, status.UsedUsd / status.CeilingUsd, costUsd);
        }
    }

    public async Task<BudgetStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var ceiling = project.BudgetCeilingUsd ?? 0m;
        var used = await db.LlmCalls.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m;
        var remaining = Math.Max(0m, ceiling - used);
        return new BudgetStatus(ceiling, used, remaining, ceiling > 0 && used >= ceiling);
    }
}
