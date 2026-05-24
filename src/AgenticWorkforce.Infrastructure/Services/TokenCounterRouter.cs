using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Routes <see cref="ITokenCounter.CountAsync"/> calls to the correct counter
/// by model-id prefix. Registered as the singleton <see cref="ITokenCounter"/>
/// in DI. Fails fast (no default tokenizer) when the model id is unrecognised —
/// different tokenizers give different counts and silent substitution would
/// corrupt every budget check downstream.
/// </summary>
internal sealed class TokenCounterRouter(
    TiktokenTokenCounter openAi,
    AnthropicTokenCounter anthropic) : ITokenCounter
{
    public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default)
    {
        ITokenCounter selected = SelectByModel(modelId);
        return selected.CountAsync(text, modelId, ct);
    }

    private ITokenCounter SelectByModel(string modelId)
    {
        if (modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            return anthropic;
        if (modelId.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("text-embedding", StringComparison.OrdinalIgnoreCase))
            return openAi;
        throw new InvalidStateException(
            $"No tokenizer registered for model '{modelId}'. Register a counter in TokenCounterRouter or extend the router (no default tokenizer fallback — different tokenizers give different counts).");
    }
}
