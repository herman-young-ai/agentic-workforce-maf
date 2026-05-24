using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_plan</c> — returns the Kanban board view of the project's
/// tasks. Used by orchestrators to see what is proposed, running, and done
/// before deciding the next step.
/// </summary>
internal sealed class GetPlanTool(
    Guid projectId,
    ITaskRepository tasks) : IPlatformTool
{
    public const string ToolName = "project.get_plan";

    [Description("Return the project's task board (id, objective, status, agent, dependencies) ordered by creation time.")]
    public async Task<string> GetPlanAsync(CancellationToken cancellationToken = default)
    {
        var board = await tasks.GetBoardAsync(projectId, cancellationToken).ConfigureAwait(false);
        var items = board.Select(t => new
        {
            id           = t.Id,
            objective    = t.Objective,
            type         = t.Type.ToString(),
            status       = t.Status.ToString(),
            agentName    = t.AgentName,
            parentTaskId = t.ParentTaskId,
            dependsOn    = t.Dependencies.Select(d => d.DependsOnTaskId).ToArray(),
            createdAt    = t.CreatedAt,
            startedAt    = t.StartedAt,
            completedAt  = t.CompletedAt,
            costUsd      = t.CostUsd
        });

        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetPlanTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetPlanTool(projectId, services.GetRequiredService<ITaskRepository>());
            return AIFunctionFactory.Create(tool.GetPlanAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
