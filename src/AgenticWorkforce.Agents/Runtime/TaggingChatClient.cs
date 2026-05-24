using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Per-execution wrapper around the shared ChatClient pipeline. Stamps the
/// AWP per-call tags onto <c>ChatOptions.AdditionalProperties</c> before
/// delegating so the middleware downstream can read project / task / agent
/// context from any call originated by this agent.
/// </summary>
internal sealed class TaggingChatClient(
    IChatClient inner,
    AgentExecutionContext context,
    string provider,
    string? agentRole) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var tagged = ChatOptionsTagger.Apply(options ?? new ChatOptions(), context, agentRole, provider, requestId: null);
        return base.GetResponseAsync(messages, tagged, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var tagged = ChatOptionsTagger.Apply(options ?? new ChatOptions(), context, agentRole, provider, requestId: null);
        return base.GetStreamingResponseAsync(messages, tagged, cancellationToken);
    }
}
