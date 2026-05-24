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
        => assembler.BuildAsync(
            context.ProjectId,
            context.Objective,
            domainTags: null,
            modelId,
            ct);
}
