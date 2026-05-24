namespace AgenticWorkforce.Agents;

/// <summary>
/// Tunable settings for the agent runtime. Bound from configuration
/// section <c>AgentRuntime</c> in the Worker (or any host) DI registration.
/// Phase 6 reads these in <see cref="Runtime.AgentRuntime"/>,
/// <see cref="Runtime.AgentFactory"/>, <see cref="Runtime.ChatClientFactory"/>,
/// <see cref="Services.LlmCallDrainService"/>, and the bounded LlmCall channel.
/// </summary>
public sealed class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    /// <summary>
    /// Wall-clock ceiling for a single ExecuteAsync invocation. Linked to the
    /// caller's CancellationToken so the caller can cancel sooner. Defaults
    /// to 30 minutes to match Principle 19 (Bounded Resource Usage —
    /// "Task execution time: 30 minutes (configurable)").
    /// </summary>
    public TimeSpan DefaultExecutionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Bound on <c>Channel&lt;LlmCall&gt;</c>. Backpressure throws
    /// <c>AuditBackpressureException</c> if the drain falls more than this
    /// many records behind (Principle 19).
    /// </summary>
    public int LlmCallChannelCapacity { get; set; } = 10_000;

    /// <summary>Drain batch size: insert up to this many rows in one SaveChanges.</summary>
    public int LlmCallDrainBatchSize { get; set; } = 500;

    /// <summary>Drain flush interval: send a partial batch if it has been this long since the last flush.</summary>
    public TimeSpan LlmCallDrainFlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum persistence retry attempts (inclusive of the first) for a single
    /// <c>LlmCall</c> batch before <see cref="Services.LlmCallDrainService"/>
    /// rethrows and crashes the host. Principle 8: the row is the source of
    /// truth for budget; silent drops are forbidden.
    /// </summary>
    public int LlmCallDrainMaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay for the drain service's exponential backoff between retries
    /// (delay = base * 2^(attempt-1)). 200 ms × {1, 2, 4, 8, 16} = ~6 s total.
    /// </summary>
    public TimeSpan LlmCallDrainRetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How many (provider, model) IChatClient pipelines to keep cached at most.</summary>
    public int MaxCachedChatClientPipelines { get; set; } = 32;

    /// <summary>Idle expiration for cached IChatClient pipelines.</summary>
    public TimeSpan ChatClientPipelineExpiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Emit a budget warning when used spend reaches this fraction of the ceiling. 0.80 = 80%.</summary>
    public decimal BudgetWarningThreshold { get; set; } = 0.80m;

    /// <summary>
    /// Default LLM provider id used when an <c>AgentCatalog</c> row has no
    /// explicit ModelConfig. Phase 6 ships with <c>"stub"</c>; Phase 7 wires
    /// real providers from per-catalog ModelConfig.
    /// </summary>
    public string DefaultProvider { get; set; } = "stub";

    /// <summary>
    /// Default model id used alongside <see cref="DefaultProvider"/>.
    /// Required: cannot be null/empty. Fails fast at DI startup if unset.
    /// </summary>
    public string DefaultModel { get; set; } = "stub-model";
}
