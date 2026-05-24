using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_team</c> — lists the agents assigned to the current project
/// with their role and enabled flag. Backed by <see cref="IProjectAgentRepository"/>
/// and <see cref="IAgentCatalogRepository"/> (the latter resolves agent_name
/// for the caller).
/// </summary>
internal sealed class GetProjectTeamTool(
    Guid projectId,
    IProjectAgentRepository projectAgents,
    IAgentCatalogRepository catalog) : IPlatformTool
{
    public const string ToolName = "project.get_team";

    [Description("List the agents assigned to the current project with their role and enabled state.")]
    public async Task<string> GetTeamAsync(CancellationToken cancellationToken = default)
    {
        var members = await projectAgents.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var team = new List<object>(members.Count);
        foreach (var pa in members)
        {
            var entry = await catalog.GetByIdAsync(pa.AgentCatalogId, cancellationToken).ConfigureAwait(false);
            team.Add(new
            {
                projectAgentId = pa.Id,
                agentCatalogId = pa.AgentCatalogId,
                agentName      = entry?.AgentName,
                role           = pa.Role.ToString(),
                enabled        = pa.Enabled,
                displayOrder   = pa.DisplayOrder
            });
        }

        return JsonSerializer.Serialize(team, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetProjectTeamTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetProjectTeamTool(
                projectId,
                services.GetRequiredService<IProjectAgentRepository>(),
                services.GetRequiredService<IAgentCatalogRepository>());
            return AIFunctionFactory.Create(tool.GetTeamAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
