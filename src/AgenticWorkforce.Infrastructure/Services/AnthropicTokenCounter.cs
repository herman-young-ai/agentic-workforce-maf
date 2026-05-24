using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Approximates Claude token counts. Anthropic's official tokenizer is not
/// vendored as a .NET package today, so we use the well-documented ~4 chars
/// per token heuristic. When the official count-tokens API or a vendored
/// BPE table becomes available, replace the implementation behind this seam
/// — interface and call sites are unchanged.
/// </summary>
internal sealed class AnthropicTokenCounter : ITokenCounter
{
    private const double CharsPerToken = 4.0;

    public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default)
    {
        var tokens = (int)Math.Ceiling(text.Length / CharsPerToken);
        return Task.FromResult(tokens);
    }
}
