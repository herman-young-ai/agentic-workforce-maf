using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_history</c> — returns the most recent project events. The
/// model picks how many to retrieve; the count is bounded by the platform's
/// <c>PlatformToolMaxPageSize</c> (Principle 19 page-size ceiling).
/// </summary>
internal sealed class GetHistoryTool(
    Guid projectId,
    IEventRepository events,
    int defaultCount,
    int maxCount) : IPlatformTool
{
    public const string ToolName = "project.get_history";

    [Description("Return the most recent project events (most recent first).")]
    public async Task<string> GetHistoryAsync(
        [Description("Number of events to retrieve. Optional; clamped to the platform's PlatformToolMaxPageSize.")] int? count = null,
        CancellationToken cancellationToken = default)
    {
        var effective = count ?? defaultCount;
        if (effective <= 0) effective = defaultCount;
        if (effective > maxCount) effective = maxCount;

        var page = await events.ListByProjectPagedAsync(
            projectId,
            new EventFilter(),
            new PagedQuery(Page: 1, PageSize: effective),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items.Select(e => new
        {
            id        = e.Id,
            type      = e.EventType,
            severity  = e.Severity.ToString(),
            taskId    = e.TaskId,
            sessionId = e.SessionId,
            source    = e.Source,
            data      = e.Data,
            createdAt = e.CreatedAt
        });

        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetHistoryTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var opts = services.GetRequiredService<IOptions<AgentRuntimeOptions>>().Value;
            var tool = new GetHistoryTool(
                projectId,
                services.GetRequiredService<IEventRepository>(),
                defaultCount: opts.PlatformToolDefaultPageSize,
                maxCount:     opts.PlatformToolMaxPageSize);
            return AIFunctionFactory.Create(tool.GetHistoryAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
