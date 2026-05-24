using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_info</c> — returns the current project's metadata.
/// projectId is captured at construction; the LLM-facing signature is empty
/// so prompt injection cannot redirect the read to another project.
/// </summary>
internal sealed class GetProjectInfoTool(
    Guid projectId,
    IProjectRepository projects) : IPlatformTool
{
    public const string ToolName = "project.get_info";

    [Description("Get the current project's metadata: name, objective, status, budget ceiling, and member/agent counts.")]
    public async Task<string> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Project", projectId);

        var result = new
        {
            id               = project.Id,
            name             = project.Name,
            objective        = project.Objective,
            status           = project.Status.ToString(),
            budgetCeilingUsd = project.BudgetCeilingUsd,
            memberCount      = project.Members?.Count ?? 0,
            agentCount       = project.Agents?.Count ?? 0,
            createdAt        = project.CreatedAt
        };
        return JsonSerializer.Serialize(result, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetProjectInfoTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetProjectInfoTool(projectId, services.GetRequiredService<IProjectRepository>());
            return AIFunctionFactory.Create(tool.GetInfoAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
