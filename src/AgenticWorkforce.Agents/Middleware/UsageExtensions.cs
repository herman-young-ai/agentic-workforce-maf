using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Middleware;

/// <summary>
/// Shared cache-token extraction. Claude surfaces cache hits via
/// <c>UsageDetails.AdditionalCounts</c> under the keys below; OpenAI does
/// not, so missing keys return zero (not an error).
/// </summary>
internal static class UsageExtensions
{
    public const string CacheReadKey   = "CacheReadInputTokens";
    public const string CacheCreateKey = "CacheCreationInputTokens";

    public static long CacheTokens(this UsageDetails? usage, string key)
    {
        if (usage?.AdditionalCounts is null) return 0;
        return usage.AdditionalCounts.TryGetValue(key, out var v) ? v : 0;
    }
}
