using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Deterministic zero-vector stub used until a real embedding provider is wired
/// up in Phase 6. Returns a 1536-dim float array of zeros to satisfy the
/// pgvector(1536) column type without making external calls.
/// </summary>
internal sealed class StubEmbeddingService : IEmbeddingService
{
    private const int Dimension = 1536;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new float[Dimension]);

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var batch = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
            batch[i] = new float[Dimension];
        return Task.FromResult(batch);
    }
}
