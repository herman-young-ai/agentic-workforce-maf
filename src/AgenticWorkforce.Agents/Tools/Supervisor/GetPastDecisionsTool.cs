using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Supervisor;

/// <summary>
/// <c>project.get_past_decisions</c> — returns the project's logged decisions
/// (Active + Superseded), enabling supervisors / planners to maintain
/// consistency with prior calls. Reversed decisions are excluded by default
/// since they no longer represent the project's position.
/// </summary>
internal sealed class GetPastDecisionsTool(
    Guid projectId,
    IDecisionRepository decisions) : IPlatformTool
{
    public const string ToolName = "project.get_past_decisions";
    private const int PageSize = 50;

    [Description("List previous decisions made on the project (ref, domain, decision, rationale, made-by, status). Reversed decisions are filtered out.")]
    public async Task<string> GetPastDecisionsAsync(CancellationToken cancellationToken = default)
    {
        var page = await decisions.ListByProjectPagedAsync(
            projectId,
            new PagedQuery(Page: 1, PageSize: PageSize),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Where(d => d.Status != Domain.Enums.DecisionStatus.Reversed)
            .Select(d => new
            {
                id           = d.Id,
                decisionRef  = d.DecisionRef,
                domain       = d.Domain,
                decision     = d.Decision,
                rationale    = d.Rationale,
                madeBy       = d.MadeBy,
                taskId       = d.TaskId,
                status       = d.Status.ToString(),
                supersededBy = d.SupersededById,
                createdAt    = d.CreatedAt
            });

        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetPastDecisionsTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetPastDecisionsTool(projectId, services.GetRequiredService<IDecisionRepository>());
            return AIFunctionFactory.Create(tool.GetPastDecisionsAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
