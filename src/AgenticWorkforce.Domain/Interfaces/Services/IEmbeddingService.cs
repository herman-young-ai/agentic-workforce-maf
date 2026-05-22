namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Embeds text into vector representations for semantic search.
/// Shared by Api search endpoints and Agents context assembly.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// True when a real embedding provider is wired (Phase 6+). False for the
    /// development stub. Endpoints that depend on embeddings short-circuit to
    /// HTTP 503 when this is false rather than throwing on first call.
    /// </summary>
    bool IsConfigured { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
