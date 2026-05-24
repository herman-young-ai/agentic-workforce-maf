using System.ComponentModel;
using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;

namespace AgenticWorkforce.Agents.Tools.Project;

/// <summary>
/// <c>project.start_research</c> — proposes a research task assigned to
/// <c>research.strategist</c>. Like <c>run_objective</c>, the task lands as
/// <see cref="TaskStatus.Proposed"/> and waits for a human approval before
/// any research agent actually runs.
/// </summary>
internal sealed class StartResearchTool(
    Guid projectId,
    ITaskRepository tasks,
    IPlatformActor actor) : IPlatformTool
{
    public const string ToolName = "project.start_research";
    private const string StrategistAgentName = "research.strategist";

    [Description("Propose a research task for the current project. The research.strategist is assigned to decompose the question into sub-queries. Returns the proposed task id; the task waits for human approval before any agent runs.")]
    public async Task<string> StartResearchAsync(
        [Description("The research question, in plain English.")] string question,
        [Description("Research depth: shallow | standard | deep. Defaults to standard.")] string? depth = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = JsonSerializer.Serialize(new
        {
            question,
            depth = depth ?? "standard"
        }, AgentJsonShapes.Options);

        var task = new AgenticTask
        {
            ProjectId   = projectId,
            Objective   = $"Research: {question}",
            AgentName   = StrategistAgentName,
            Type        = TaskType.AgentTask,
            Status      = TaskStatus.Proposed,
            Source      = TaskSource.AdHoc,
            Inputs      = inputs,
            CreatedById = actor.UserId
        };

        var added = await tasks.AddAsync(task, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            taskId    = added.Id,
            agentName = added.AgentName,
            status    = added.Status.ToString()
        }, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => StartResearchTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new StartResearchTool(
                projectId,
                services.GetRequiredService<ITaskRepository>(),
                services.GetRequiredService<IPlatformActor>());
            return AIFunctionFactory.Create(tool.StartResearchAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
