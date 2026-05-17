namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Embeds text into vector representations for semantic search.
/// Shared by Api search endpoints and Agents context assembly.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
