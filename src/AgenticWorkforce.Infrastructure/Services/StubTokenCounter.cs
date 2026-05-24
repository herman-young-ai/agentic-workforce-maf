using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Counts tokens for the development "stub-*" model family used by
/// <see cref="AgenticWorkforce.Agents.Runtime.StubChatClient"/>. Uses the
/// same ~4 chars/token heuristic as <see cref="AnthropicTokenCounter"/> —
/// the count only feeds the pre-check budget estimate, and there is no
/// real model behind it. Exists so the Phase 6 stub pipeline can run
/// end-to-end without provisioning Tiktoken or Anthropic counters.
/// </summary>
internal sealed class StubTokenCounter : ITokenCounter
{
    private const double CharsPerToken = 4.0;

    public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default)
        => Task.FromResult((int)Math.Ceiling(text.Length / CharsPerToken));
}
