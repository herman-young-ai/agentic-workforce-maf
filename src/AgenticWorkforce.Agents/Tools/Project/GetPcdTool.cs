using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_pcd</c> — returns the current project's Project Context
/// Document (principles, guardrails, current state). The PCD is the agent's
/// authoritative source of project-level direction.
/// </summary>
internal sealed class GetPcdTool(
    Guid projectId,
    IProjectContextService pcd) : IPlatformTool
{
    public const string ToolName = "project.get_pcd";

    [Description("Return the project's PCD (principles, guardrails, current state) as a single JSON document.")]
    public async Task<string> GetPcdAsync(CancellationToken cancellationToken = default)
    {
        var ctx = await pcd.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        var result = new
        {
            contextVersion  = ctx.ContextVersion,
            sizeCharacters  = ctx.SizeCharacters,
            sizeTokens      = ctx.SizeTokens,
            contextData     = ctx.ContextData
        };
        return JsonSerializer.Serialize(result, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetPcdTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetPcdTool(projectId, services.GetRequiredService<IProjectContextService>());
            return AIFunctionFactory.Create(tool.GetPcdAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
