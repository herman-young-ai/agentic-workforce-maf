using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using AgenticWorkforce.Domain.Queries;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_history</c> — returns the most recent project events. The
/// model picks how many to retrieve (capped at 100, the Principle 19 page-size
/// ceiling), but never the project — that is captured at construction.
/// </summary>
internal sealed class GetHistoryTool(
    Guid projectId,
    IEventRepository events) : IPlatformTool
{
    public const string ToolName = "project.get_history";
    private const int MaxCount = 100;

    [Description("Return the most recent project events (most recent first). The count is capped at 100.")]
    public async Task<string> GetHistoryAsync(
        [Description("Number of events to retrieve; values above 100 are clamped to 100.")] int count = 50,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) count = 50;
        if (count > MaxCount) count = MaxCount;

        var page = await events.ListByProjectPagedAsync(
            projectId,
            new EventFilter(),
            new PagedQuery(Page: 1, PageSize: count),
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
            var tool = new GetHistoryTool(projectId, services.GetRequiredService<IEventRepository>());
            return AIFunctionFactory.Create(tool.GetHistoryAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
