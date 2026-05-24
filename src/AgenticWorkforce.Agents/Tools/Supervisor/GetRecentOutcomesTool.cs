using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Supervisor;

/// <summary>
/// <c>project.get_recent_outcomes</c> — supervisor-side view of the most recent
/// completed/failed tasks. Used by <c>project.supervisor</c> to decide whether
/// a failure looks transient or persistent before recommending retry/escalate.
/// </summary>
internal sealed class GetRecentOutcomesTool(
    Guid projectId,
    ITaskRepository tasks) : IPlatformTool
{
    public const string ToolName = "project.get_recent_outcomes";
    private const int MaxCount = 50;

    [Description("List the most recent terminal tasks (completed/failed/cancelled) with id, status, agent, cost, and duration. Capped at 50.")]
    public async Task<string> GetRecentOutcomesAsync(
        [Description("How many recent outcomes to return; capped at 50.")] int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) count = 20;
        if (count > MaxCount) count = MaxCount;

        var page = await tasks.ListByProjectPagedAsync(
            projectId,
            new TaskListFilter(),
            new PagedQuery(Page: 1, PageSize: count),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Where(t => t.CompletedAt is not null)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => new
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
            var tool = new GetRecentOutcomesTool(projectId, services.GetRequiredService<ITaskRepository>());
            return AIFunctionFactory.Create(tool.GetRecentOutcomesAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
