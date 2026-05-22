using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Fail-fast stub used until a real embedding provider is wired in Phase 6
/// (Azure OpenAI per ADR-002). Returning a zero vector silently corrupts
/// pgvector cosine search (undefined distance over a zero-norm vector), so
/// the stub throws if called. Embedding-dependent endpoints inspect
/// <see cref="IEmbeddingService.IsConfigured"/> and short-circuit to HTTP 503
/// before invoking, so callers never see the exception under normal use.
/// </summary>
internal sealed class StubEmbeddingService : IEmbeddingService
{
    private const string NotConfiguredMessage =
        "Embeddings are not configured. Wire AzureOpenAIEmbeddingService in Phase 6 " +
        "(per ADR-002) before calling embedding-dependent endpoints.";

    public bool IsConfigured => false;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => throw new NotImplementedException(NotConfiguredMessage);

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => throw new NotImplementedException(NotConfiguredMessage);
}
