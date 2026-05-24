using AgenticWorkforce.Agents.Runtime;

namespace AgenticWorkforce.Agents.Context;

/// <summary>
/// Builds per-execution <see cref="ProjectContextProvider"/> instances. The
/// provider injects per-turn context (PCD, learnings, task) on each call
/// into the IChatClient pipeline.
/// </summary>
internal interface IProjectContextProviderFactory
{
    ProjectContextProvider Create(AgentExecutionContext context, string modelId);
}
