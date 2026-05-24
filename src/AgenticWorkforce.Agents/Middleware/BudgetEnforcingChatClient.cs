using System.Text;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Middleware;

internal enum BudgetClientMode
{
    /// <summary>Outermost pipeline slot. Rejects if budget already exhausted.</summary>
    PreCheckOnly,

    /// <summary>Per-iteration spend recording. Sits inside FunctionInvokingChatClient.</summary>
    RecordSpend
}

internal sealed class BudgetEnforcingChatClient(
    IChatClient inner,
    IBudgetService budgets,
    IModelPricingService pricing,
    ITokenCounter tokens,
    BudgetClientMode mode) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = ChatOptionsTagger.Read(options);
        var model = options?.ModelId
            ?? throw new InvalidStateException("ChatOptions.ModelId is required for budget enforcement.");

        if (mode == BudgetClientMode.PreCheckOnly)
        {
            var serialised = SerializeMessages(messages);
            var estimatedInput = await tokens.CountAsync(serialised, model, cancellationToken).ConfigureAwait(false);
            var estimatedCost = await pricing.EstimateInputCostAsync(model, estimatedInput, cancellationToken).ConfigureAwait(false);
            if (!await budgets.CanSpendAsync(ctx.ProjectId, ctx.SessionId, estimatedCost, cancellationToken).ConfigureAwait(false))
                throw new BudgetExceededException("project", ctx.ProjectId.ToString(), 0);
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var usage = response.Usage;
        var actualCost = await pricing.CalculateCostAsync(
            model,
            input:       usage?.InputTokenCount  ?? 0,
            output:      usage?.OutputTokenCount ?? 0,
            cacheRead:   usage.CacheTokens(UsageExtensions.CacheReadKey),
            cacheCreate: usage.CacheTokens(UsageExtensions.CacheCreateKey),
            cancellationToken).ConfigureAwait(false);
        await budgets.RecordSpendAsync(ctx.ProjectId, ctx.SessionId, ctx.TaskId, actualCost, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static string SerializeMessages(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            sb.Append('[').Append(m.Role.Value).Append("]\n").Append(m.Text).Append('\n');
        }
        return sb.ToString();
    }
}
