using System.Diagnostics;
using System.Threading.Channels;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Middleware;

/// <summary>
/// Records one <see cref="LlmCall"/> per provider call. Sits inside
/// <c>FunctionInvokingChatClient</c> so each tool-loop iteration produces
/// its own row (per Phase 6 plan §5.2).
/// </summary>
internal sealed class CostTrackingChatClient(
    IChatClient inner,
    IModelPricingService pricing,
    ChannelWriter<LlmCall> llmCallWriter,
    TimeProvider clock) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = ChatOptionsTagger.Read(options);
        var model = options?.ModelId
            ?? throw new InvalidStateException("ChatOptions.ModelId is required for cost tracking.");

        var start = clock.GetTimestamp();
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var elapsed = clock.GetElapsedTime(start);

        var usage = response.Usage;
        var inputTokens   = usage?.InputTokenCount  ?? 0;
        var outputTokens  = usage?.OutputTokenCount ?? 0;
        var cacheRead     = usage.CacheTokens(UsageExtensions.CacheReadKey);
        var cacheCreate   = usage.CacheTokens(UsageExtensions.CacheCreateKey);

        var cost = await pricing.CalculateCostAsync(
            model, inputTokens, outputTokens, cacheRead, cacheCreate, cancellationToken).ConfigureAwait(false);

        var record = new LlmCall
        {
            ProjectId            = ctx.ProjectId,
            SessionId            = ctx.SessionId,
            TaskId               = ctx.TaskId,
            AgentName            = ctx.AgentName,
            AgentRole            = ctx.AgentRole,
            Model                = model,
            Provider             = ctx.Provider,
            InputTokens          = inputTokens,
            OutputTokens         = outputTokens,
            CacheReadTokens      = cacheRead,
            CacheCreationTokens  = cacheCreate,
            CostUsd              = cost,
            LatencyMs            = (int)elapsed.TotalMilliseconds,
            RequestId            = ctx.RequestId,
            ToolCount            = CountTools(response)
        };

        if (!llmCallWriter.TryWrite(record))
            throw new AuditBackpressureException(
                $"LlmCall channel full while recording cost for project {ctx.ProjectId} (drain service backpressured).");

        return response;
    }

    private static int CountTools(ChatResponse response)
    {
        var count = 0;
        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent) count++;
            }
        }
        return count;
    }
}
