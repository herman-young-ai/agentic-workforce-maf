using AgenticWorkforce.Domain.Agents;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Builds per-execution <see cref="AITool"/> instances for the Platform tools
/// referenced in an agent's manifest. Platform tools capture
/// <c>projectId</c> at construction so the LLM-facing parameter list never
/// contains it — prompt injection cannot redirect a tool to another project.
/// </summary>
internal interface IPlatformToolResolver
{
    /// <summary>
    /// Resolves the subset of <paramref name="manifest"/> that maps to known
    /// Platform tools. Unknown bindings are ignored (the sandbox / MCP path
    /// handles them — 7d/7e). The caller composes the two lists in
    /// <see cref="Runtime.AgentFactory"/>.
    /// </summary>
    IList<AITool> Resolve(IReadOnlyList<AgentToolBindingShape> manifest, Guid projectId);
}
