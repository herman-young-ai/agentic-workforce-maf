using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.add_principle</c> — appends a principle to the project's PCD.
/// Attribution is the platform service-account actor; humans can retract
/// agent-added principles via the existing retract path (Principle 13).
/// </summary>
internal sealed class AddPrincipleTool(
    Guid projectId,
    IProjectContextService pcd,
    IPlatformActor actor) : IPlatformTool
{
    public const string ToolName = "project.add_principle";

    [Description("Append a principle to the project's PCD. The principle becomes immediately readable by other agents. A human can retract it later.")]
    public async Task<string> AddPrincipleAsync(
        [Description("The principle text; one sentence stating a project-wide rule or constraint.")] string principle,
        CancellationToken cancellationToken = default)
    {
        var principleId = await pcd.AddPrincipleAsync(projectId, principle, actor.UserId, cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            principleId,
            addedBy = actor.Email
        }, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => AddPrincipleTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new AddPrincipleTool(
                projectId,
                services.GetRequiredService<IProjectContextService>(),
                services.GetRequiredService<IPlatformActor>());
            return AIFunctionFactory.Create(tool.AddPrincipleAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
