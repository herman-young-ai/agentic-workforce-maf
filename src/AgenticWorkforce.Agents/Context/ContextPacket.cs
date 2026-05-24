using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Context;

/// <summary>
/// Output of <see cref="IContextAssembler"/>: an ordered list of system
/// messages prepended to each agent turn, plus diagnostic counters so the
/// caller can verify what fitted into the token budget.
/// </summary>
internal sealed record ContextPacket(
    IReadOnlyList<ChatMessage> Messages,
    int EstimatedTokens,
    int LayersIncluded);
