using AgenticWorkforce.Agents.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Agents.Middleware;

/// <summary>
/// Phase 6 placeholder: logs every call at Information level with the tags
/// from <see cref="ChatOptionsTagger"/>. Phase 9 will replace the body with
/// a non-blocking channel write to Event Hubs + WORM blob storage.
/// </summary>
internal sealed class AuditingChatClient(
    IChatClient inner,
    ILogger<AuditingChatClient> logger) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = ChatOptionsTagger.Read(options);
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "AgentCall project={ProjectId} task={TaskId} agent={AgentName} model={ModelId} provider={Provider} in={InputTokens} out={OutputTokens}",
            ctx.ProjectId, ctx.TaskId, ctx.AgentName, options?.ModelId, ctx.Provider,
            response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

        return response;
    }
}
