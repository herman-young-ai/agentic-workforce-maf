namespace AgenticWorkforce.Agents.Context;

/// <summary>
/// Builds the per-turn context packet (PCD + task definition + learnings +
/// trimmed history) within a token budget. Priority-ordered per ADR-010:
/// PCD &amp; current task are never trimmed.
/// </summary>
internal interface IContextAssembler
{
    Task<ContextPacket> BuildAsync(
        Guid projectId,
        string? taskDefinition,
        string[]? domainTags,
        string modelId,
        CancellationToken ct = default);
}
