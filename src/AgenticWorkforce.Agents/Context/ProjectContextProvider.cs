using AgenticWorkforce.Agents.Runtime;

namespace AgenticWorkforce.Agents.Context;

/// <summary>
/// Per-execution wrapper that calls <see cref="IContextAssembler"/> on demand.
/// Phase 6 returns the packet directly; Phase 7+ will plug this into MAF's
/// AIContextProvider so the per-turn context is recomputed each iteration.
/// </summary>
internal sealed class ProjectContextProvider(
    IContextAssembler assembler,
    AgentExecutionContext context,
    string modelId)
{
    public Task<ContextPacket> GetCurrentPacketAsync(CancellationToken ct = default)
        // Phase 7 will load the AgenticTask row by context.TaskId and pass its
        // definition. Until then we pass null — labelling the agent's Objective
        // as "## Current Task" would mislead the assembled prompt.
        => assembler.BuildAsync(
            context.ProjectId,
            taskDefinition: null,
            domainTags: null,
            modelId,
            ct);
}
