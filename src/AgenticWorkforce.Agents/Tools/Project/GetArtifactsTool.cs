using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_artifacts</c> — returns the project's polished deliverables
/// (reports, generated documents, code). Retracted artifacts are excluded;
/// they remain in the DB but should not surface back to the agent (Principle 13).
/// </summary>
internal sealed class GetArtifactsTool(
    Guid projectId,
    IArtifactRepository artifacts) : IPlatformTool
{
    public const string ToolName = "project.get_artifacts";
    private const int PageSize = 50;

    [Description("List the project's artifacts (id, title, type, agent that produced it, created/retracted timestamps). Retracted artifacts are filtered out.")]
    public async Task<string> ListArtifactsAsync(CancellationToken cancellationToken = default)
    {
        var page = await artifacts.ListByProjectPagedAsync(
            projectId,
            new PagedQuery(Page: 1, PageSize: PageSize),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Where(a => a.RetractedAt is null)
            .Select(a => new
            {
                id            = a.Id,
                title         = a.Title,
                artifactType  = a.ArtifactType.ToString(),
                contentFormat = a.ContentFormat.ToString(),
                taskId        = a.TaskId,
                agentName     = a.AgentName,
                fileSizeBytes = a.FileSizeBytes,
                createdAt     = a.CreatedAt
            });
        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetArtifactsTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetArtifactsTool(projectId, services.GetRequiredService<IArtifactRepository>());
            return AIFunctionFactory.Create(tool.ListArtifactsAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
