using AgenticWorkforce.Domain.Entities;
using Microsoft.Agents.AI;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Builds a fully-configured MAF agent from a catalog entry + the execution
/// context. Implementation is the canonical 6-step construction described in
/// 007-agent-implementation.md §3.2.
/// </summary>
internal interface IAgentFactory
{
    Task<AIAgent> CreateAsync(
        AgentCatalog catalog,
        Project project,
        ProjectAgent? projectAgent,
        AgentExecutionContext context,
        CancellationToken ct = default);
}
