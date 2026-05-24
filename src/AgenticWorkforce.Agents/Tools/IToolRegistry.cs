using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Resolves an explicit tool manifest to a list of <see cref="AITool"/>
/// instances that the agent can call. Empty manifest =&gt; zero tools
/// (Principle 14: Secure by Default).
/// </summary>
internal interface IToolRegistry
{
    void Register(ToolBinding binding, AITool tool);

    IList<AITool> Resolve(IReadOnlyList<ToolBinding> manifest);
}
