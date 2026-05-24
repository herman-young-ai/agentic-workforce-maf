using System.Threading.Channels;
using AgenticWorkforce.Agents.Middleware;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Bounded LRU-style cache of fully-decorated IChatClient pipelines, keyed by
/// (provider, model). Pipeline order per Phase 6 plan §5:
///
///   BudgetEnforcing.PreCheckOnly  (outermost)
///     -> FunctionInvocation       (MAF built-in — tool loop)
///         -> Auditing
///         -> CostTracking
///         -> BudgetEnforcing.RecordSpend
///         -> ContentSafety
///         -> OpenTelemetry        (inner-most decoration)
///         -> Raw provider         (StubChatClient in Phase 6)
///
/// Owns a dedicated <see cref="MemoryCache"/> rather than sharing the
/// DI-registered <c>IMemoryCache</c> because pipeline eviction semantics
/// (hard count cap + sliding expiry) differ from the unbounded default
/// the rest of the app uses. The cache is internal — no other code reads it.
/// </summary>
internal sealed class ChatClientFactory : IChatClientFactory, IDisposable
{
    private readonly IBudgetService _budgets;
    private readonly IModelPricingService _pricing;
    private readonly ITokenCounter _tokens;
    private readonly ChannelWriter<LlmCall> _llmCallWriter;
    private readonly TimeProvider _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly AgentRuntimeOptions _opts;
    private readonly MemoryCache _cache;

    public ChatClientFactory(
        IBudgetService budgets,
        IModelPricingService pricing,
        ITokenCounter tokens,
        ChannelWriter<LlmCall> llmCallWriter,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        IOptions<AgentRuntimeOptions> options)
    {
        _budgets = budgets;
        _pricing = pricing;
        _tokens = tokens;
        _llmCallWriter = llmCallWriter;
        _clock = clock;
        _loggerFactory = loggerFactory;
        _opts = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _opts.MaxCachedChatClientPipelines });
    }

    public IChatClient GetOrCreate(string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return _cache.GetOrCreate($"{provider}:{model}", entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = _opts.ChatClientPipelineExpiration;
            return BuildPipeline(provider, model);
        })!;
    }

    private IChatClient BuildPipeline(string provider, string model)
    {
        // Raw provider — StubChatClient until real Foundry/Azure OpenAI credentials are wired.
        IChatClient pipeline = new StubChatClient();

        // Inner -> outer (each wraps the previous)
        pipeline = new OpenTelemetryChatClient(pipeline, sourceName: $"awp.agent.{provider}");
        pipeline = new ContentSafetyChatClient(pipeline);
        pipeline = new BudgetEnforcingChatClient(pipeline, _budgets, _pricing, _tokens, BudgetClientMode.RecordSpend);
        pipeline = new CostTrackingChatClient(pipeline, _pricing, _llmCallWriter, _clock);
        pipeline = new AuditingChatClient(pipeline, _loggerFactory.CreateLogger<AuditingChatClient>());

        // FunctionInvocation owns the tool loop. Everything above sees one aggregate call per agent turn;
        // everything below sees each tool-loop iteration. The split BudgetEnforcing client (PreCheck above,
        // RecordSpend below) is intentional — pre-check rejects exhausted budgets without entering the loop;
        // record-spend writes per-iteration spend so the budget tracks reality.
        pipeline = new FunctionInvokingChatClient(pipeline);
        pipeline = new BudgetEnforcingChatClient(pipeline, _budgets, _pricing, _tokens, BudgetClientMode.PreCheckOnly);

        return pipeline;
    }

    public void Dispose() => _cache.Dispose();
}
