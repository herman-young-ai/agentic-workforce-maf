namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Counts tokens for the given text under the given model's tokenizer.
/// One overload only — Domain stays MAF-free. Agents-side callers serialise
/// messages to a role-tagged string before counting.
/// Implementations live in Infrastructure (Tiktoken for OpenAI families,
/// Anthropic for Claude). The router throws if no implementation matches
/// the model id — never falls back to a default tokenizer because different
/// tokenizers give different counts and silent substitution would corrupt
/// budget math (Principle 8 — fail fast).
/// </summary>
public interface ITokenCounter
{
    Task<int> CountAsync(string text, string modelId, CancellationToken ct = default);
}
