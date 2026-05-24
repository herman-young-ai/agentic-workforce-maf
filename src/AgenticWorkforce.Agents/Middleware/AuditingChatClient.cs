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

        // LoggerMessage.Define caps at 6 type args, so the token counts are folded into
        // a structured "io" tuple string — still single-allocation in the logger but
        // keeps the call within the supported overload set.
        var io = $"in={response.Usage?.InputTokenCount ?? 0} out={response.Usage?.OutputTokenCount ?? 0}";
        LogAgentCall(logger,
            ctx.ProjectId, ctx.TaskId, ctx.AgentName ?? string.Empty,
            options?.ModelId ?? string.Empty, ctx.Provider, io, null);

        return response;
    }

    private static readonly Action<ILogger, Guid, Guid?, string, string, string, string, Exception?> LogAgentCall =
        LoggerMessage.Define<Guid, Guid?, string, string, string, string>(LogLevel.Information,
            new EventId(1, nameof(LogAgentCall)),
            "AgentCall project={ProjectId} task={TaskId} agent={AgentName} model={ModelId} provider={Provider} usage={Usage}");
}
