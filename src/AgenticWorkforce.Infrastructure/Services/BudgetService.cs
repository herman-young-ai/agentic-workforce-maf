using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Events;
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
///
/// <para><b>Event emission (Principle 19)</b></para>
/// <see cref="RecordSpendAsync"/> emits a durable <see cref="ProjectEvent"/>
/// (<see cref="EventTypes.BudgetWarning"/> at threshold, <see cref="EventTypes.BudgetExhausted"/>
/// when used &gt;= ceiling). Events are persisted via the publisher's transactional
/// outbox and fanned out to SignalR clients post-commit, so the UI gets a live
/// signal without coupling the budget tracker to the transport.
/// </summary>
internal sealed class BudgetService(
    AppDbContext db,
    IEventPublisher events,
    IOptions<BudgetServiceOptions> options,
    ILogger<BudgetService> logger) : IBudgetService
{
    private readonly decimal _warnThreshold = options.Value.WarningThreshold;

    public async Task<bool> CanSpendAsync(
        Guid projectId, Guid? sessionId, decimal estimatedCostUsd, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(projectId, ct);
        // Principle 14 (Secure by Default — Unix model): missing config denies.
        // A project without an explicit BudgetCeilingUsd cannot run agents.
        if (status.CeilingUsd <= 0) return false;
        if (status.IsExhausted) return false;
        return status.RemainingUsd >= estimatedCostUsd;
    }

    public async Task RecordSpendAsync(
        Guid projectId, Guid? sessionId, Guid? taskId, decimal costUsd, CancellationToken ct = default)
    {
        // Spend is the sum of LlmCall rows; CostTrackingChatClient has already written the row.
        // This hook publishes the threshold-crossing event so operators see live warnings.
        var status = await GetStatusAsync(projectId, ct);
        if (status.CeilingUsd <= 0) return;

        if (status.IsExhausted)
        {
            await PublishBudgetEventAsync(
                projectId, taskId, EventTypes.BudgetExhausted, EventSeverity.Error,
                status, costUsd, ct).ConfigureAwait(false);
            LogExhausted(logger, projectId, status.UsedUsd, status.CeilingUsd, null);
            return;
        }

        if (status.UsedUsd / status.CeilingUsd >= _warnThreshold)
        {
            await PublishBudgetEventAsync(
                projectId, taskId, EventTypes.BudgetWarning, EventSeverity.Warning,
                status, costUsd, ct).ConfigureAwait(false);
            LogWarning(logger, projectId, status.UsedUsd, status.CeilingUsd, costUsd, null);
        }
    }

    private async Task PublishBudgetEventAsync(
        Guid projectId, Guid? taskId, string eventType, EventSeverity severity,
        BudgetStatus status, decimal lastSpendUsd, CancellationToken ct)
    {
        // Phase 6 emits warnings from the agent execution path (no API request user). The
        // source field is the platform identifier; Phase 8 will attach the triggering
        // workflow / actor when those flow through the execution context.
        var evt = taskId.HasValue
            ? ProjectEventBuilder.ForTask(projectId, taskId.Value, eventType, "platform:budget", new
            {
                ceilingUsd     = status.CeilingUsd,
                usedUsd        = status.UsedUsd,
                remainingUsd   = status.RemainingUsd,
                lastSpendUsd,
                utilisation    = status.CeilingUsd > 0 ? status.UsedUsd / status.CeilingUsd : 0m
            }, severity)
            : ProjectEventBuilder.ForProject(projectId, eventType, "platform:budget", new
            {
                ceilingUsd     = status.CeilingUsd,
                usedUsd        = status.UsedUsd,
                remainingUsd   = status.RemainingUsd,
                lastSpendUsd,
                utilisation    = status.CeilingUsd > 0 ? status.UsedUsd / status.CeilingUsd : 0m
            }, severity);

        await events.PublishAsync(evt, ct).ConfigureAwait(false);
        // The IEventPublisher contract requires the caller to SaveChanges; the publisher
        // only stages the row to the active DbContext.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static readonly Action<ILogger, Guid, decimal, decimal, decimal, Exception?> LogWarning =
        LoggerMessage.Define<Guid, decimal, decimal, decimal>(LogLevel.Warning,
            new EventId(1, nameof(LogWarning)),
            "Budget warning for project {ProjectId}: {Used:F2}/{Ceiling:F2} USD; last spend {LastSpend:F4}.");

    private static readonly Action<ILogger, Guid, decimal, decimal, Exception?> LogExhausted =
        LoggerMessage.Define<Guid, decimal, decimal>(LogLevel.Critical,
            new EventId(2, nameof(LogExhausted)),
            "Budget exhausted for project {ProjectId}: {Used:F2}/{Ceiling:F2} USD.");

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
