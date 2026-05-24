using AgenticWorkforce.Agents.Runtime;

namespace AgenticWorkforce.Agents.Context;

internal sealed class ProjectContextProviderFactory(IContextAssembler assembler)
    : IProjectContextProviderFactory
{
    public ProjectContextProvider Create(AgentExecutionContext context, string modelId)
        => new(assembler, context, modelId);
}
