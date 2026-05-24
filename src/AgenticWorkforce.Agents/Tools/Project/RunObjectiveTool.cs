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
/// <c>project.run_objective</c> — creates a proposed ad-hoc <see cref="AgenticTask"/>
/// for the current project. Per Principle 17 the task lands in
/// <see cref="TaskStatus.Proposed"/>; a human approves it before any execution
/// runs. The platform service-account user owns the audit attribution.
/// </summary>
internal sealed class RunObjectiveTool(
    Guid projectId,
    ITaskRepository tasks,
    IPlatformActor actor) : IPlatformTool
{
    public const string ToolName = "project.run_objective";

    [Description("Propose an ad-hoc task for the current project. Returns the created task id. The task is created as Proposed and requires human approval before it executes.")]
    public async Task<string> RunObjectiveAsync(
        [Description("What the task should achieve, in plain English.")] string objective,
        [Description("The agent that should execute the task (must exist on the project team).")] string agentName,
        CancellationToken cancellationToken = default)
    {
        var task = new AgenticTask
        {
            ProjectId   = projectId,
            Objective   = objective,
            AgentName   = agentName,
            Type        = TaskType.AgentTask,
            Status      = TaskStatus.Proposed,
            Source      = TaskSource.AdHoc,
            CreatedById = actor.UserId
        };

        var added = await tasks.AddAsync(task, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            taskId = added.Id,
            status = added.Status.ToString()
        }, AgentJsonShapes.Options);
    }

    internal sealed class Factory : IPlatformToolFactory
    {
        public string ToolName => RunObjectiveTool.ToolName;
        public AITool Create(IServiceProvider services, Guid projectId)
        {
            var tool = new RunObjectiveTool(
                projectId,
                services.GetRequiredService<ITaskRepository>(),
                services.GetRequiredService<IPlatformActor>());
            return AIFunctionFactory.Create(tool.RunObjectiveAsync, new AIFunctionFactoryOptions { Name = ToolName });
        }
    }
}
