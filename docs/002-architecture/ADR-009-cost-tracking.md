# ADR-009: Cost Tracking and Budget Enforcement

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R09-response-cost-tracking.md](../098-research/R09-response-cost-tracking.md)

---

## Context

The Agentic Workforce Platform runs multiple AI agents per project, each making LLM calls against paid providers (Claude, GPT). We need real-time per-call cost recording, hierarchical budget enforcement (per-agent, per-execution, per-session, per-project), and FinOps dashboards. Cost overruns in a bank are unacceptable. **Accuracy and predictability are paramount — the system fails fast and loud rather than silently degrading quality.**

## Decision

**`DelegatingChatClient` middleware on the `IChatClient` pipeline — NOT at the MAF agent layer**

### Why IChatClient, not AIAgent middleware

Every MAF `ChatClientAgent` calls an `IChatClient`. The `FunctionInvokingChatClient` re-enters `GetResponseAsync` once per tool round-trip. A budget middleware placed **inside** the function invocation loop (i.e., wrapping the raw provider client) sees **every individual model call** — which is what you need for accurate per-call cost recording. The agent-level `AgentRunResponse.Usage` is a framework roll-up that may understate spend.

### Implementation: `BudgetEnforcingChatClient`

```csharp
public sealed class BudgetEnforcingChatClient : DelegatingChatClient
{
    private readonly IBudgetService _budget;

    public BudgetEnforcingChatClient(IChatClient inner, IBudgetService budget)
        : base(inner) { _budget = budget; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct)
    {
        var ctx = ProjectContext.Current;

        // Pre-call: fail fast if budget exceeded
        options = options?.Clone() ?? new ChatOptions();
        _budget.EnforceOrThrow(ctx);

        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, ct);
        sw.Stop();

        // Post-call: record actual cost
        if (response.Usage is { } u)
        {
            long cacheRead  = ReadCount(u, "cache_read_input_tokens", "CacheReadInputTokens");
            long cacheWrite = ReadCount(u, "cache_creation_input_tokens", "CacheCreationInputTokens");

            await _budget.RecordAndEnforceAsync(new LlmCallCost
            {
                ProjectId = ctx.ProjectId,
                AgentName = ctx.AgentName,
                Model = response.ModelId ?? options.ModelId,
                InputTokens = u.InputTokenCount ?? 0,
                OutputTokens = u.OutputTokenCount ?? 0,
                CacheReadTokens = cacheRead,
                CacheWriteTokens = cacheWrite,
                LatencyMs = sw.ElapsedMilliseconds,
            });
        }
        return response;
    }
}
```

### Pipeline Wiring (order matters)

```csharp
IChatClient client = anthropicClient.AsIChatClient("claude-sonnet-4-6")
    .AsBuilder()
    .Use((inner, sp) => new BudgetEnforcingChatClient(inner, sp.GetRequiredService<IBudgetService>()))
    .UseFunctionInvocation()    // tool loop OUTSIDE budget → each model call is metered
    .UseOpenTelemetry()         // OTel outermost for full-pipeline traces
    .Build(serviceProvider);
```

Builder order is bottom-up: `Build()` returns `OTel(FunctionInvocation(Budget(inner)))`. Each model call inside the tool loop hits Budget first.

### Token Usage Extraction

`ChatResponse.Usage` is a strongly-typed `UsageDetails`:

| Property | Type | Source |
|----------|------|--------|
| `InputTokenCount` | `long?` | Standard — all providers |
| `OutputTokenCount` | `long?` | Standard — all providers |
| `TotalTokenCount` | `long?` | Standard — all providers |
| `AdditionalCounts["cache_read_input_tokens"]` | `long` | Anthropic (official SDK, snake_case) |
| `AdditionalCounts["CacheReadInputTokens"]` | `long` | Anthropic (community SDK, PascalCase) |
| `AdditionalCounts["cache_creation_input_tokens"]` | `long` | Anthropic (official SDK) |
| `AdditionalCounts["CacheCreationInputTokens"]` | `long` | Anthropic (community SDK) |

**Must probe both naming conventions** — keys differ by SDK:

```csharp
static long ReadCount(UsageDetails u, params string[] keys) =>
    keys.Where(k => u.AdditionalCounts?.ContainsKey(k) == true)
        .Select(k => u.AdditionalCounts![k]).FirstOrDefault();
```

### Budget Hierarchy

| Level | Enforcement | Mechanism |
|-------|-------------|-----------|
| Per-call | Record cost | `BudgetEnforcingChatClient` post-call |
| Per-agent per-execution | Hard stop at ceiling ($1.00 default) | `BudgetService` cumulative check |
| Per-execution | Hard stop | `BudgetService` cumulative check |
| Per-session | Warning at 80%, hard stop at 100% ($50 default) | `BudgetService` + notification event |
| Per-project | Warning at 80%, hard stop at 100% | `BudgetService` + notification event |
| Per-hour platform-wide | Alert only ($5/hr) | Background monitor + alert |

### Fail-Fast Budget Enforcement

**There is no model downgrade.** When budget is exceeded, the execution fails immediately with a clear error. Accuracy and predictability are paramount — a wrong answer from a cheaper model is worse than a stopped execution.

```csharp
public void EnforceOrThrow(ProjectContext ctx)
{
    var budget = GetBudgetState(ctx.ProjectId);
    if (budget.UsagePercent >= 100)
        throw new BudgetExceededException(ctx.ProjectId, budget);
    if (budget.UsagePercent >= 80)
        _eventBus.Publish(new BudgetWarningEvent(ctx.ProjectId, budget));
}
```

Budget warnings at 80% notify users via SignalR so they can proactively extend the budget before the hard stop fires.

### Model Pricing Table (EF Core)

```csharp
[PrimaryKey(nameof(Model), nameof(EffectiveFrom))]
public class ModelPricing
{
    public string Model { get; set; } = "";
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public decimal PricePerMtokInput { get; set; }
    public decimal PricePerMtokOutput { get; set; }
    public decimal? PricePerMtokCacheRead { get; set; }
    public decimal? PricePerMtokCacheCreate { get; set; }
}

// Resolve current price
var price = await db.ModelPricing
    .Where(p => p.Model == model && p.EffectiveFrom <= now
                && (p.EffectiveTo == null || p.EffectiveTo > now))
    .OrderByDescending(p => p.EffectiveFrom)
    .FirstAsync();
```

### Cost Calculation

```csharp
decimal CalculateCost(ModelPricing price, long input, long output, long cacheRead, long cacheWrite)
{
    var regularInput = input - cacheRead - cacheWrite;
    return (regularInput * price.PricePerMtokInput / 1_000_000m)
         + (output * price.PricePerMtokOutput / 1_000_000m)
         + (cacheRead * (price.PricePerMtokCacheRead ?? price.PricePerMtokInput * 0.1m) / 1_000_000m)
         + (cacheWrite * (price.PricePerMtokCacheCreate ?? price.PricePerMtokInput * 1.25m) / 1_000_000m);
}
```

### Cost Aggregation Queries

```csharp
// Total cost per agent for a project in last 24 hours
var agentCosts = await db.LlmCalls
    .Where(c => c.ProjectId == projectId && c.CreatedAt >= cutoff)
    .GroupBy(c => c.AgentName)
    .Select(g => new { Agent = g.Key, Total = g.Sum(c => c.CostUsd) })
    .ToListAsync();

// Hourly cost timeline
var timeline = await db.LlmCalls
    .Where(c => c.ProjectId == projectId && c.CreatedAt >= since)
    .GroupBy(c => new { c.CreatedAt.Date, c.CreatedAt.Hour })
    .Select(g => new { Hour = g.Min(c => c.CreatedAt), Cost = g.Sum(c => c.CostUsd) })
    .OrderBy(x => x.Hour)
    .ToListAsync();

// Cache hit rate per agent
var cacheStats = await db.LlmCalls
    .Where(c => c.ProjectId == projectId)
    .GroupBy(c => c.AgentName)
    .Select(g => new {
        Agent = g.Key,
        HitRate = g.Sum(c => c.CacheReadTokens) * 1.0 / g.Sum(c => c.InputTokens)
    }).ToListAsync();
```

### Project Context via AsyncLocal

```csharp
public sealed class ProjectContext
{
    private static readonly AsyncLocal<ProjectContext?> _current = new();
    public static ProjectContext? Current => _current.Value;
    public static IDisposable Set(ProjectContext ctx) { _current.Value = ctx; return ...; }

    public Guid ProjectId { get; init; }
    public string AgentName { get; init; } = "";
    public Guid? ExecutionId { get; init; }
    public string DefaultModel { get; init; } = "claude-sonnet-4-6";
}
```

Set by the workflow orchestrator before each agent invocation, read by the `BudgetEnforcingChatClient`.

## Consequences

- Token estimation pre-call is approximate — no synchronous "count tokens before send" in `IChatClient`; post-call reconciliation is source of truth
- `Microsoft.Agents.AI.Anthropic` is still preview — cache token key names may drift; code defensively with multi-key lookup
- `AgentRunResponse.Usage` aggregation across tool-loop iterations is implementation-defined — do NOT rely on it for billing; use per-call `ChatResponse.Usage` from the middleware
- Streaming responses: some providers emit no usage; set `streamUsage: true` on Anthropic; have fallback local token counting
- `ChatOptions` may be mutated by the framework — always `Clone()` before modifying in middleware
- MAF has no built-in "project" or "budget" concept — this is entirely our responsibility
- OpenTelemetry `UseOpenTelemetry()` is for ops observability, not billing telemetry — build custom `Meter("Bank.AI.Cost")` with project/tenant/model tags

### Principle Compliance

- **P14 Secure by Default:** New agents/projects default to zero budget until explicitly configured. No LLM spend without a human-set budget ceiling. Deny-by-default = $0 until configured.
- **P16 Single Source of Truth:** The `ModelPricing` table is the single authoritative source for pricing — no hardcoded prices elsewhere, no client-side cost calculations. Cost dashboards derive from the `LlmCall` table.
- **P17 Human Authority:** Humans can override a budget hard-stop via emergency budget extension. Humans can manually adjust recorded costs if a pricing error is discovered. The system fails fast but the human decides what happens next.
- **P18 Idempotency:** Cost recording is idempotent — if the same LLM call result is recorded twice (retry after transient failure), the budget does not double-count. `LlmCall` records are keyed by execution ID + sequence number.
- **P19 Bounded Resource Usage:** `RecordAndEnforceAsync` DB calls have explicit timeouts. Budget ceilings are hard bounds. Per-agent tool call limits prevent runaway cost accumulation between LLM calls.
- **P20 Version Everything:** The `LlmCallCost` schema and cost API response format are versioned to handle future additions (new token types, new pricing dimensions) without breaking consumers.
- **P21 Explicit Over Implicit:** Budget enforcement middleware position in the IChatClient pipeline is explicitly declared and documented as an architectural constraint, not just a code comment.
