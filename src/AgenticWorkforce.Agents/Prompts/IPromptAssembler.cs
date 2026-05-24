using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Agents.Prompts;

/// <summary>
/// Composes the static 5-layer Instructions string for an agent.
/// Per-turn dynamic context (PCD, learnings, task inputs) is injected
/// separately via AIContextProvider, not through Instructions.
/// </summary>
internal interface IPromptAssembler
{
    Task<string> AssembleAsync(
        AgentCatalog agent,
        Project project,
        ProjectAgent? projectAgent,
        CancellationToken ct = default);
}
