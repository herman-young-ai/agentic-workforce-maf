using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Tools.Supervisor;

/// <summary>
/// <c>project.get_recent_outcomes</c> — supervisor-side view of the most recent
/// terminal tasks. Used by <c>project.supervisor</c> to decide whether a failure
/// looks transient or persistent before recommending retry/escalate.
///
/// <para><b>Why a dedicated repository query</b></para>
/// Reading the latest page of tasks ordered by creation date and then filtering
/// for <c>CompletedAt != null</c> in memory is buggy when many in-flight tasks
/// sit between the request and the most recent outcomes — the tool would return
/// zero results despite plenty of completed work. <see cref="ITaskRepository.ListRecentOutcomesAsync"/>
/// filters by terminal status server-side and orders by <c>CompletedAt</c> so
/// the tool always returns the latest <i>N</i> outcomes.
/// </summary>
internal sealed class GetRecentOutcomesTool(
    Guid projectId,
    ITaskRepository tasks,
    int defaultCount,
    int maxCount) : IPlatformTool
{
    public const string ToolName = "project.get_recent_outcomes";

    [Description("List the most recent terminal tasks (Completed/Failed/Cancelled) ordered by completion time, with id, status, agent, cost, duration, and retry count.")]
    public async Task<string> GetRecentOutcomesAsync(
        [Description("How many recent outcomes to return. Optional; bounded by the platform's PlatformToolMaxPageSize.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        var effective = count ?? defaultCount;
        if (effective <= 0) effective = defaultCount;
        if (effective > maxCount) effective = maxCount;

        var rows = await tasks.ListRecentOutcomesAsync(projectId, effective, cancellationToken).ConfigureAwait(false);

        var items = rows.Select(t => new
        {
            id              = t.Id,
            status          = t.Status.ToString(),
            agentName       = t.AgentName,
            objective       = t.Objective,
            completedAt     = t.CompletedAt,
            durationSeconds = t.DurationSeconds,
            costUsd         = t.CostUsd,
            retryCount      = t.RetryCount
        });

        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetRecentOutcomesTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var opts = services.GetRequiredService<IOptions<AgentRuntimeOptions>>().Value;
            var tool = new GetRecentOutcomesTool(
                projectId,
                services.GetRequiredService<ITaskRepository>(),
                defaultCount: opts.PlatformToolDefaultPageSize,
                maxCount:     opts.PlatformToolMaxPageSize);
            return AIFunctionFactory.Create(tool.GetRecentOutcomesAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
