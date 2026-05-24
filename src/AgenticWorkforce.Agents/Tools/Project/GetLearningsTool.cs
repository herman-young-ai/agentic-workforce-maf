using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.get_learnings</c> — returns the project's active learnings
/// (status = Active), ordered by confidence. Retracted learnings are excluded
/// (Principle 13 — they exist but are no longer authoritative).
/// </summary>
internal sealed class GetLearningsTool(
    Guid projectId,
    ILearningRepository learnings) : IPlatformTool
{
    public const string ToolName = "project.get_learnings";
    private const int PageSize = 50;

    [Description("List the project's active learnings (id, title, body, confidence, evidence). Retracted/pending learnings are not returned.")]
    public async Task<string> GetLearningsAsync(CancellationToken cancellationToken = default)
    {
        var page = await learnings.ListByProjectPagedAsync(
            projectId,
            new PagedQuery(Page: 1, PageSize: PageSize),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Where(l => l.Status == LearningStatus.Active)
            .OrderByDescending(l => l.Confidence)
            .Select(l => new
            {
                id         = l.Id,
                title      = l.Title,
                body       = l.Body,
                confidence = l.Confidence,
                evidence   = l.Evidence,
                createdAt  = l.CreatedAt
            });
        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => GetLearningsTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new GetLearningsTool(projectId, services.GetRequiredService<ILearningRepository>());
            return AIFunctionFactory.Create(tool.GetLearningsAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
