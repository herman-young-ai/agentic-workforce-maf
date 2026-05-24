using System.Collections.Concurrent;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.ML.Tokenizers;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Tokenizer for OpenAI / Azure OpenAI families. Delegates to
/// <see cref="TiktokenTokenizer.CreateForModel(string, System.Collections.Generic.IReadOnlyDictionary{string, int}?, Normalizer?)"/>
/// which maps the model id to the correct encoding (o200k_base for 4o / 4.1 / o1 / o3,
/// cl100k_base for earlier GPT-4 / 3.5, etc.).
/// </summary>
internal sealed class TiktokenTokenCounter : ITokenCounter
{
    private readonly ConcurrentDictionary<string, Tokenizer> _tokenizers = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default)
    {
        var tokenizer = _tokenizers.GetOrAdd(modelId, BuildTokenizer);
        return Task.FromResult(tokenizer.CountTokens(text));
    }

    private static Tokenizer BuildTokenizer(string modelId)
    {
        try
        {
            return TiktokenTokenizer.CreateForModel(modelId);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidStateException(
                $"Tiktoken does not recognise model '{modelId}'. Either upgrade Microsoft.ML.Tokenizers or extend TokenCounterRouter with a fallback. Inner: {ex.Message}");
        }
    }
}
