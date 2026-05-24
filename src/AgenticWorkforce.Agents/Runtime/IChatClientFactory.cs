using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Provides shared, fully-decorated <see cref="IChatClient"/> pipelines per
/// (provider, model) pair. One pipeline instance is reused across all
/// concurrent agent executions on that pair (DelegatingChatClient is
/// thread-safe).
/// </summary>
internal interface IChatClientFactory
{
    IChatClient GetOrCreate(string provider, string model);
}
