using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.list_workflows</c> — returns the workflow definitions defined
/// on the current project. Read-only; running a workflow lands in Phase 8
/// alongside the workflow engine.
/// </summary>
internal sealed class ListWorkflowsTool(
    Guid projectId,
    IWorkflowDefinitionRepository workflows) : IPlatformTool
{
    public const string ToolName = "project.list_workflows";
    private const int PageSize = 50;

    [Description("List workflow definitions available on the current project (id, name, version, enabled flag, locked flag).")]
    public async Task<string> ListAsync(CancellationToken cancellationToken = default)
    {
        var page = await workflows.ListByProjectPagedAsync(
            projectId,
            new PagedQuery(Page: 1, PageSize: PageSize),
            cancellationToken).ConfigureAwait(false);

        var items = page.Items.Select(w => new
        {
            id          = w.Id,
            name        = w.Name,
            description = w.Description,
            version     = w.Version,
            enabled     = w.Enabled,
            locked      = w.LockedAt is not null,
            designedBy  = w.DesignedBy,
            createdAt   = w.CreatedAt
        });
        return JsonSerializer.Serialize(items, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => ListWorkflowsTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new ListWorkflowsTool(projectId, services.GetRequiredService<IWorkflowDefinitionRepository>());
            return AIFunctionFactory.Create(tool.ListAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
