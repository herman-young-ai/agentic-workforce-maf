# Microsoft Agent Framework (MAF) for .NET — Implementation Reference for a Cost Tracking & Budget Enforcement Layer (May 2026)

## TL;DR

- **The right interception point is `IChatClient` middleware in `Microsoft.Extensions.AI` (MEAI), not the MAF agent layer.** Every MAF agent (`ChatClientAgent`) ultimately calls an `IChatClient`. You wrap that client with `ChatClientBuilder` / `DelegatingChatClient` so a single budget-enforcement decorator sees every model call (including each iteration of the `FunctionInvokingChatClient` tool loop). The middleware is a `Func<IEnumerable<ChatMessage>, ChatOptions?, IChatClient, CancellationToken, Task<ChatResponse>>` (and a streaming counterpart) — registered via `.AsBuilder().Use(...)` or by subclassing `DelegatingChatClient`.
- **Token counts are first-class but provider-dependent in practice.** `ChatResponse.Usage` is a strongly-typed `UsageDetails` (`InputTokenCount`, `OutputTokenCount`, `TotalTokenCount`, all `long?`, plus an `AdditionalCounts` dictionary of `long`). Anthropic's `cache_read_input_tokens` and `cache_creation_input_tokens` are mapped into `UsageDetails.AdditionalCounts` by both the community `Anthropic.SDK` (`ChatClientHelper.CreateUsageDetails`) and the official `Anthropic` 12.x SDK's `AsIChatClient`, but the dictionary keys are provider-specific strings — your code must know to look them up. Streaming responses surface usage as a `UsageContent` item in the final `ChatResponseUpdate.Contents`.
- **Dynamic model selection works at the `ChatOptions.ModelId` level, but with caveats.** You can mutate `ChatOptions.ModelId` (a settable `string?`) from inside a middleware before forwarding the call. However, several concrete `IChatClient` adapters (notably Azure OpenAI, where the deployment name is baked into the underlying `ChatClient` from `Azure.AI.OpenAI`) ignore `ModelId` and route to a fixed deployment — so for genuine model swapping in a banking platform you must keep a registry of provider-specific `IChatClient` instances and dispatch between them inside your middleware, not rely on `ModelId` alone.

---

## Key Findings

### 1. Versions and packages you should pin (May 2026)

| Package | Version | Notes |
|---|---|---|
| `Microsoft.Agents.AI` | **1.5.0** stable (1.0 GA shipped 3 April 2026) | Core agent abstractions: `AIAgent`, `ChatClientAgent`, `AgentSession`, `AgentRunResponse`. |
| `Microsoft.Agents.AI.Abstractions` | matches core | Public types only; pure dependency for libraries that produce agents. |
| `Microsoft.Agents.AI.OpenAI` | 1.x stable | `AsAIAgent()` helpers on `OpenAIClient`. |
| `Microsoft.Agents.AI.Anthropic` | 1.3.0-preview.260423.1 (latest preview); 1.0.0-rc5 most recent stable-track release at GA | Builds on the official `Anthropic` NuGet (≥12.13.0). Still flagged preview. |
| `Microsoft.Agents.AI.Foundry` | preview | For Foundry-hosted agents. |
| `Microsoft.Extensions.AI` | **10.5.2** stable | High-level `ChatClientBuilder`, `Use*` middleware extensions, OpenTelemetry, function invocation, distributed cache. |
| `Microsoft.Extensions.AI.Abstractions` | 10.5.2 stable | `IChatClient`, `DelegatingChatClient`, `ChatMessage`, `ChatOptions`, `ChatResponse`, `ChatResponseUpdate`, `UsageDetails`, `AdditionalPropertiesDictionary`, `AIContent` family. |
| `Anthropic` (official Claude SDK for C#) | 12.20.0 stable | Implements `AsIChatClient(model)`; β labelled but the integration is the supported path. |
| `Anthropic.SDK` (tghamm community) | 5.10.0 | Alternative; richer prompt-caching helpers. |

MAF GA explicitly took a hard dependency on the **stable 10.4.1+** line of `Microsoft.Extensions.AI`. The 9.x preview namespace is gone.

### 2. `IChatClient` middleware pattern — the exact API surface

The core interface (`Microsoft.Extensions.AI.IChatClient` in `Microsoft.Extensions.AI.Abstractions`):

```csharp
public interface IChatClient : IDisposable
{
    Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    object? GetService(Type serviceType, object? serviceKey = null);
}
```

Documented thread-safety contract: *"All members of `IChatClient` are thread-safe for concurrent use. It is expected that all implementations of `IChatClient` support being used by multiple requests concurrently."* This is what allows a single decorated client to back many concurrent agents/sessions.

#### `DelegatingChatClient` (decorator base class)

```csharp
public class DelegatingChatClient : IDisposable, IChatClient
{
    protected DelegatingChatClient(IChatClient innerClient);
    protected IChatClient InnerClient { get; }
    public virtual Task<ChatResponse> GetResponseAsync(...);
    public virtual IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(...);
    public virtual object? GetService(Type, object? = null);
    public void Dispose();
}
```

Microsoft Learn: *"This is recommended as a base type when building clients that can be chained around an underlying `IChatClient`. The default implementation simply passes each call to the inner client instance."* Use this for your `BudgetEnforcingChatClient` so you also get correct `GetService` forwarding (important — MAF and downstream middleware probe inner clients via `GetService` for `ChatClientMetadata`, `FunctionInvokingChatClient`, etc.).

#### `ChatClientBuilder` and `Use(...)` overloads

```csharp
public sealed class ChatClientBuilder
{
    public ChatClientBuilder(IChatClient innerClient);
    public ChatClientBuilder(Func<IServiceProvider, IChatClient> innerClientFactory);

    // factory-style overloads
    public ChatClientBuilder Use(Func<IChatClient, IChatClient> clientFactory);
    public ChatClientBuilder Use(Func<IChatClient, IServiceProvider, IChatClient> clientFactory);

    // anonymous delegating handler (separate non-streaming + streaming)
    public ChatClientBuilder Use(
        Func<IEnumerable<ChatMessage>, ChatOptions?, IChatClient, CancellationToken,
             Task<ChatResponse>>? getResponseFunc,
        Func<IEnumerable<ChatMessage>, ChatOptions?, IChatClient, CancellationToken,
             IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponseFunc);

    // shared (pre/post only) — does not let you intercept output
    public ChatClientBuilder Use(
        Func<IEnumerable<ChatMessage>, ChatOptions?,
             Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task>,
             CancellationToken, Task> sharedFunc);

    public IChatClient Build(IServiceProvider? services = null);
}
```

Bootstrap any `IChatClient` into a builder with the extension method `IChatClient.AsBuilder()` (defined on `ChatClientBuilderChatClientExtensions`).

Built-in middleware extensions in `Microsoft.Extensions.AI`:
- `UseFunctionInvocation()` → `FunctionInvokingChatClient` (the auto-tool-call loop). **Critical:** this middleware re-enters `GetResponseAsync` once per tool round-trip, so a budget middleware placed *outside* it sees only the final aggregated response, while one placed *inside* (i.e. registered before it) sees each individual model call. For per-call cost enforcement, register your budget middleware **before** `UseFunctionInvocation()` in builder order so it ends up wrapping the inner provider client directly.
- `UseOpenTelemetry(sourceName, configure)` → records prompt/completion tokens as span attributes and as counters on the `Microsoft.Extensions.AI` meter.
- `UseDistributedCache(IDistributedCache)` → response cache.
- `ConfigureOptions(Action<ChatOptions>)` → mutates `ChatOptions` per call (this is the documented, MEAI-blessed way to inject defaults — but a custom `DelegatingChatClient` is what you want for *conditional* mutation).
- `UseLogging(ILoggerFactory)` → `LoggingChatClient`.

#### MAF-level chat middleware (function form)

The MAF agent layer adds three middleware *types* — Agent-run, Function-call, and "Chat client" — but the chat client variant is implemented as a thin wrapper over the same `Microsoft.Extensions.AI` builder chain. The function signature you wire in via `.AsBuilder().Use(getResponseFunc: ..., getStreamingResponseFunc: null)` (or the `clientFactory` argument on `AsAIAgent`) is:

```csharp
async Task<ChatResponse> CustomChatClientMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerChatClient,
    CancellationToken cancellationToken)
{
    // pre-call: inspect/modify options (e.g., enforce budget, swap ModelId)
    var response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);
    // post-call: response.Usage is populated here
    return response;
}
```

This is the canonical sample shown in Microsoft Learn's *Adding middleware to agents* page.

### 3. `ChatResponse`, `ChatResponseUpdate`, `UsageDetails` — exact shape

`ChatResponse` (Microsoft.Extensions.AI.Abstractions):

| Property | Type | Notes |
|---|---|---|
| `Messages` | `IList<ChatMessage>` | One typical, multiple when there are tool round-trips. |
| `Usage` | `UsageDetails?` | Nullable — provider may not emit it. |
| `ResponseId` | `string?` | |
| `ConversationId` | `string?` | Used when service stores history (Foundry, OpenAI Responses with `store=true`). |
| `ModelId` | `string?` | Echo of model used. |
| `CreatedAt` | `DateTimeOffset?` | |
| `FinishReason` | `ChatFinishReason?` | |
| `AdditionalProperties` | `AdditionalPropertiesDictionary?` (`IDictionary<string, object?>`) | Free-form provider data. |
| `RawRepresentation` | `object?` | Provider-native object (cast for last-resort access — not stable across versions). |
| `Text` | computed | Concatenated `TextContent`. |
| `ContinuationToken` | `ResponseContinuationToken?` | For background/long-running responses. |

`UsageDetails`:

```csharp
public class UsageDetails
{
    public long? InputTokenCount  { get; set; }
    public long? OutputTokenCount { get; set; }
    public long? TotalTokenCount  { get; set; }
    public AdditionalPropertiesDictionary<long>? AdditionalCounts { get; set; }
}
```

`AdditionalCounts` is a typed dictionary (`<string, long>`) — not the looser `AdditionalProperties`. This is where providers stuff things like:
- Azure OpenAI: `"ReasoningTokenCount"` (o1/o-series), `cached_tokens`.
- Ollama: `load_duration`, `total_duration`, `prompt_eval_duration`, `eval_duration` (note: not actually token counts, but the framework permits any `long`).
- **Anthropic:** `cache_creation_input_tokens` and `cache_read_input_tokens` (see §6).

Per Microsoft documentation: *"To make it possible to avoid collisions between similarly-named, but unrelated, additional counts between different AI services, any keys not explicitly defined here should be prefixed with the name of the AI service, e.g., 'openai.' or 'azure.'."* In practice the existing OpenAI and Anthropic adapters ship the **unprefixed** Anthropic/OpenAI native field names — so your cost calculator should be tolerant of both prefixed and unprefixed keys.

`ChatResponseUpdate` (streaming): the final update typically carries a `UsageContent` in its `Contents` list:

```csharp
public sealed class UsageContent : AIContent
{
    public UsageContent(UsageDetails details);
    public UsageDetails Details { get; set; }
}
```

To recover usage from a streamed run, scan `update.Contents.OfType<UsageContent>().LastOrDefault()?.Details`. There is also a helper `ChatResponse.FromAsyncEnumerable(...)` (and `ToChatResponseAsync` extension) that aggregates updates and surfaces usage on the rolled-up `ChatResponse`.

### 4. `ChatOptions` — exact properties (Microsoft.Extensions.AI.Abstractions 10.4+)

From `dotnet/extensions` source:

```csharp
public class ChatOptions
{
    public string? ConversationId         { get; set; }
    public string? Instructions            { get; set; }
    public string? ModelId                 { get; set; }
    public float?  Temperature             { get; set; }
    public float?  TopP                    { get; set; }
    public int?    TopK                    { get; set; }
    public float?  FrequencyPenalty        { get; set; }
    public float?  PresencePenalty         { get; set; }
    public int?    MaxOutputTokens         { get; set; }
    public long?   Seed                    { get; set; }
    public ChatResponseFormat? ResponseFormat { get; set; }
    public IList<string>? StopSequences    { get; set; }
    public bool?   AllowMultipleToolCalls  { get; set; }
    public bool?   AllowBackgroundResponses{ get; set; }
    public ChatToolMode? ToolMode          { get; set; }
    public IList<AITool>? Tools            { get; set; }   // [JsonIgnore]
    public ChatReasoning? Reasoning        { get; set; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
    public Func<object?>? RawRepresentationFactory { get; set; } // for provider-specific options
    public ResponseContinuationToken? ContinuationToken { get; set; } // [Experimental]

    public virtual ChatOptions Clone();   // shallow copy of collections
}
```

**Mutability inside middleware is supported and idiomatic.** Per the IChatClient docs: *"implementations of IChatClient might mutate the arguments supplied to GetResponseAsync … such as by configuring the options instance."* You can therefore do this safely from your budget middleware:

```csharp
options ??= new ChatOptions();
options.ModelId = SelectModelForBudget(options.ModelId, remainingBudgetUsd);
options.MaxOutputTokens = Math.Min(options.MaxOutputTokens ?? int.MaxValue, hardCap);
```

⚠ Beware of two pitfalls:
1. The same `ChatOptions` instance may be reused across the `FunctionInvokingChatClient` loop — modifying it once "sticks" for subsequent tool round-trips, which is usually desirable for budget enforcement but may be surprising. If you need per-iteration overrides, `Clone()` first.
2. `RawRepresentationFactory`, when set by an outer caller (e.g. to pass `OpenAI.Chat.ChatCompletionOptions` or Anthropic `MessageParameters` directly), can override the strongly-typed properties depending on the adapter. For deterministic budget enforcement, prefer applying caps on `MaxOutputTokens` and on `ModelId` rather than relying on raw-passthrough options.

### 5. MAF agent lifecycle — exact .NET classes and call shape

Hierarchy (all in `Microsoft.Agents.AI`):

```
AIAgent (abstract)
  └─ ChatClientAgent
       └─ ChatClientAgent<T> (typed structured-output variant)
  ├─ A2AAgent (remote agent over Agent-to-Agent protocol)
  └─ workflow.AsAIAgent(...) wrappers

AgentSession (abstract)
  └─ ChatClientAgentSession (exposes ConversationId)

AgentRunResponse  / AgentRunResponseUpdate
ChatClientAgentRunResponse<T>

ChatClientAgentOptions
ChatClientAgentRunOptions : AgentRunOptions
```

Constructors / factory methods:

```csharp
// Direct
var agent = new ChatClientAgent(
    chatClient,                        // any IChatClient
    instructions: "...",
    name: "Assistant",
    description: "...",
    tools: new List<AITool>{ AIFunctionFactory.Create(MyMethod) });

// With full options
var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = "Assistant",
    Description = "...",
    ChatOptions = new ChatOptions { /* model, temperature, tools */ },
    ChatHistoryProvider  = new InMemoryChatHistoryProvider(),
    AIContextProviders   = [ new MyMemoryProvider() ],
    UseProvidedChatClientAsIs = false   // default: framework adds default middleware
});

// Provider extension methods (most common in samples)
AIAgent agent = openAiClient.GetChatClient("gpt-4o-mini")
    .AsAIAgent(instructions: "...", name: "Joker");

AIAgent agent = anthropicClient.AsAIAgent(
    model: "claude-haiku-4-5", instructions: "...", name: "...");
```

Execution methods on `AIAgent`:

```csharp
Task<AgentRunResponse> RunAsync(
    string                       message,
    AgentSession?                session = null,
    AgentRunOptions?             options = null,
    CancellationToken            ct = default);

Task<AgentRunResponse> RunAsync(
    IEnumerable<ChatMessage>     messages,
    AgentSession?                session = null,
    AgentRunOptions?             options = null,
    CancellationToken            ct = default);

IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(...);

AgentSession GetNewThread();             // legacy synonym for CreateSessionAsync
ValueTask<AgentSession> CreateSessionAsync(...);
```

`AgentRunOptions` / `ChatClientAgentRunOptions` (the bridge to `ChatOptions` at runtime):

```csharp
public class AgentRunOptions
{
    public bool? AllowBackgroundResponses { get; set; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
    // … (provider-/agent-specific extensions live in subclasses)
}

public class ChatClientAgentRunOptions : AgentRunOptions
{
    public ChatClientAgentRunOptions();
    public ChatClientAgentRunOptions(ChatOptions chatOptions);
    public ChatOptions? ChatOptions { get; set; }
}
```

This is the GA-blessed mechanism to override `ChatOptions` per call:
```csharp
await agent.RunAsync(input, options: new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions { Temperature = 0.7f, ModelId = "gpt-5-mini" }
});
```
Internally `ChatClientAgent` performs *options merging* between `ChatClientAgentOptions.ChatOptions` (agent-level defaults), `ChatClientAgentRunOptions.ChatOptions` (per-run), and any options the caller already built — see the `ChatClientAgent_ChatOptionsMergingTests.cs` fixture in the source tree.

`AgentRunResponse` exposes:
- `Messages` (`IList<ChatMessage>`) — every message produced (including intermediate tool calls/results)
- `Text` (aggregated assistant text)
- `Usage` (`UsageDetails?`) — aggregated across all model calls in the run, when the underlying chat client emitted usage on each `ChatResponse`
- `RawRepresentation`
- `AdditionalProperties`
- `ResponseId`, `CreatedAt`

For multi-call (tool-loop) runs, `AgentRunResponse.Usage` is the framework's roll-up. **For accurate per-call cost telemetry, do not rely solely on the agent-level roll-up — inspect `ChatResponse.Usage` inside your `IChatClient` middleware**, because:
- Some adapters omit usage on intermediate tool-call responses, so the roll-up may understate spend.
- Cache read/write tokens (Anthropic) are in `AdditionalCounts`, and the framework's aggregation logic for `AdditionalCounts` across multiple `ChatResponse` instances is implementation-defined and provider-specific. Your decorator is the only place you reliably see them per round-trip.

### 6. Anthropic provider details — cache tokens in `AdditionalCounts`

There are **two** Anthropic `IChatClient` implementations in the .NET ecosystem; both surface cache tokens through the same MEAI extension point but in slightly different ways:

#### a) Official `Anthropic` SDK (12.x) + `Microsoft.Agents.AI.Anthropic`
- `AnthropicClient` exposes `AsIChatClient(model)` (defined in `Anthropic` ≥ 10.0) returning an `IChatClient` whose `ChatResponse.Usage.AdditionalCounts` contains the native fields from the Anthropic API `usage` object (`cache_creation_input_tokens`, `cache_read_input_tokens`, plus the new sub-object fields `ephemeral_5m_input_tokens` / `ephemeral_1h_input_tokens` from the 1-hour TTL feature).
- `Microsoft.Agents.AI.Anthropic` (1.0.0-rc5 / 1.3.0-preview.260423.1 in May 2026) adds `AnthropicClientExtensions.AsAIAgent(this IAnthropicClient client, string model, …, Func<IChatClient,IChatClient>? clientFactory = null, …)`. The `clientFactory` parameter is exactly where you inject your budget middleware so it's between MAF's defaults and the raw Anthropic transport. A second flavor — `AnthropicFoundryClient` with `AnthropicFoundryApiKeyCredentials` or `AnthropicFoundryIdentityTokenCredentials` — targets Claude models hosted on Microsoft Foundry (`/anthropic/v1/messages`).

#### b) Community `Anthropic.SDK` 5.x (tghamm)
Per the published source map (DeepWiki of `tghamm/Anthropic.SDK`, `ChatClientHelper.cs` lines 20-30): `CreateUsageDetails()` does the following mapping:
- `Usage.InputTokens`             → `UsageDetails.InputTokenCount`
- `Usage.OutputTokens`            → `UsageDetails.OutputTokenCount`
- `Usage.CacheCreationInputTokens` → `AdditionalCounts["CacheCreationInputTokens"]` (PascalCase key)
- `Usage.CacheReadInputTokens`    → `AdditionalCounts["CacheReadInputTokens"]` (PascalCase key)

Anthropic rate-limit headers (`RequestsLimit`, `RequestsRemaining`, `RequestsReset`, `TokensLimit`, `TokensRemaining`, `TokensReset`, `RetryAfter`) are stored on `ChatMessage.AdditionalProperties` rather than on `UsageDetails` — useful for circuit-breaking but a separate code path.

#### Practical implication for a banking budget engine
Your cost layer must:
1. **Probe both naming conventions** when reading the `AdditionalCounts` dictionary, e.g.:
```csharp
long Read(IDictionary<string,long>? d, params string[] keys)
   => keys.Where(k => d?.ContainsKey(k) == true).Select(k => d![k]).FirstOrDefault();
var cacheRead = Read(usage.AdditionalCounts,
   "cache_read_input_tokens", "CacheReadInputTokens");
var cacheWrite = Read(usage.AdditionalCounts,
   "cache_creation_input_tokens", "CacheCreationInputTokens");
```
2. **Apply Anthropic's pricing multipliers in your code**, not the framework's: cache reads are billed at 0.1× base input; 5-min cache writes 1.25×; 1-hour cache writes 2×. The framework only forwards the raw counts.
3. **Watch the 5-minute default TTL drift** (Anthropic silently changed from 1h to 5min on 6 March 2026) — set `cache_control.ttl` explicitly in the raw representation if you depend on it.
4. **Verify Foundry-hosted Claude** still passes through cache counts. As of May 2026 the elbruno community shim and `AnthropicFoundryClient` both report cache usage, but the Foundry Agent Service `create_agent` portal pattern does not officially list Claude — your code should treat that path as unsupported.

### 7. Concurrency model

- **`IChatClient` is required to be thread-safe** for concurrent calls (interface contract). A single shared decorated client is the right pattern.
- **`AgentSession` is the unit of conversation isolation.** Sessions are lightweight per-conversation containers; the documentation explicitly states "thousands of concurrent sessions without cross-contamination" is supported. Each session owns its own `ChatHistoryProvider` state and (for service-managed history) `ConversationId`. The same `AIAgent` instance is meant to be reused across sessions — it holds no per-conversation state.
- **Tool-call loop concurrency.** `FunctionInvokingChatClient` honours `ChatOptions.AllowMultipleToolCalls` — when the model returns multiple parallel tool calls in one assistant message, the framework may execute them concurrently. Your function middleware therefore needs to be reentrant.
- **Workflow concurrency.** `Microsoft.Agents.AI.Workflows` uses a **Pregel-style superstep model**: within a superstep, all triggered executors run in parallel; the workflow advances only when every executor in the superstep completes. Concurrent multi-agent patterns are built with `WorkflowBuilder.AddFanOutEdge(start, [a, b, c]).AddFanInEdge(merge, [a, b, c])` or the higher-level `AgentWorkflowBuilder.BuildConcurrent()` / `ConcurrentBuilder` (Python) / `AddFanInBarrierEdge`. Crucially: **all parallel agents in a superstep share the same `IChatClient` pipeline**, so a budget middleware registered once on that client sees every agent's calls — exactly what you want for a mission-wide budget.
- **No "mission" type ships in MAF as of 1.x.** What you'd call a *mission* you'd typically model as either (a) a workflow with a bounded set of agents and a `WorkflowContext`, or (b) a parent-coordinator pattern where each child agent gets its own `AgentSession` but they all share an `IChatClient` carrying a per-mission budget context (e.g., via `AsyncLocal<MissionBudget>` set by the request handler and read by your `DelegatingChatClient`). This is the approach the Microsoft sample ApimGateway/Foundry telemetry blog uses externally; MAF doesn't impose one.

### 8. Reference shape for a budget-enforcement middleware

```csharp
public sealed class BudgetEnforcingChatClient : DelegatingChatClient
{
    private readonly IBudgetService _budget;

    public BudgetEnforcingChatClient(IChatClient inner, IBudgetService budget)
        : base(inner) { _budget = budget; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var ctx = MissionContext.Current ??
                  throw new InvalidOperationException("No mission scope.");

        // Pre-call: hard reservation
        var estIn = TokenEstimator.Estimate(messages);
        var maxOut = options?.MaxOutputTokens ?? ctx.DefaultMaxOutput;
        await _budget.ReserveAsync(ctx.MissionId, options?.ModelId, estIn, maxOut, ct);

        // Optional: dynamic model swap on remaining budget
        options = options is null ? new ChatOptions() : options.Clone();
        options.ModelId = _budget.SelectModel(ctx, options.ModelId);
        options.MaxOutputTokens = Math.Min(maxOut, ctx.HardCapTokens);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, ct);
        }
        catch
        {
            await _budget.RollbackAsync(ctx.MissionId, ct);
            throw;
        }

        // Post-call: settle on actuals
        var u = response.Usage;
        if (u is not null)
        {
            long cacheRead  = ReadCount(u, "cache_read_input_tokens",
                                          "CacheReadInputTokens");
            long cacheWrite = ReadCount(u, "cache_creation_input_tokens",
                                          "CacheCreationInputTokens");
            await _budget.SettleAsync(
                ctx.MissionId, response.ModelId ?? options.ModelId,
                u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0,
                cacheRead, cacheWrite, ct);
        }
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // … same shape, accumulate UsageContent from updates …
        await foreach (var upd in base.GetStreamingResponseAsync(messages, options, ct))
            yield return upd;
    }

    static long ReadCount(UsageDetails u, params string[] keys) =>
        keys.Where(k => u.AdditionalCounts?.ContainsKey(k) == true)
            .Select(k => u.AdditionalCounts![k]).FirstOrDefault();
}
```

Wire-up:

```csharp
IChatClient client = anthropicClient.AsIChatClient("claude-haiku-4-5")
    .AsBuilder()
    .Use((inner, sp) => new BudgetEnforcingChatClient(inner, sp.GetRequiredService<IBudgetService>()))
    .UseFunctionInvocation()        // tool loop sits OUTSIDE budget so each model call is metered
    .UseOpenTelemetry()             // OTel sits outside everything for full-pipeline traces
    .Build(serviceProvider);

AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
{
    Name = "RiskAnalyst",
    ChatOptions = new ChatOptions { Instructions = "...", ModelId = "claude-haiku-4-5" }
});
```

(Builder order is bottom-up: `Build()` returns `OTel(FunctionInvocation(Budget(inner)))`. Each model call inside the tool loop hits `Budget` first, which is what you want. If you instead want budget *outside* the tool loop — i.e., one charge per agent.RunAsync — swap the order.)

---

## Caveats and Known Limitations for a Banking Use Case

1. **Token estimation pre-call is approximate.** No `IChatClient` adapter offers a synchronous "count tokens before send". For Anthropic, the underlying SDK has a dedicated `Messages.CountTokensAsync` endpoint, but it isn't surfaced through `IChatClient`; you'd call it via `inner.GetService(typeof(AnthropicClient))`. For OpenAI you'd use `Microsoft.ML.Tokenizers`. Treat the pre-call reservation as an estimate, with strict post-call reconciliation as the source of truth.
2. **`Microsoft.Agents.AI.Anthropic` is still in preview at 1.0.0-rc5 / 1.3.0-preview.260423.1** as of May 2026 — `Microsoft.Agents.AI` core is GA 1.x but the Anthropic connector is not. The official `Anthropic` C# SDK itself is also marked beta on platform.claude.com. Pin exact versions and budget for breaking changes; the `AsIChatClient(model)` method shape and the dictionary keys in `AdditionalCounts` are the most likely surfaces to drift.
3. **`ModelId` is not always honoured.** `Azure.AI.OpenAI` adapters bind to a specific deployment at construction; setting `ChatOptions.ModelId` doesn't redirect the request. For real model-switching across cost tiers, maintain a registry `Dictionary<string, IChatClient>` keyed by model and dispatch in your middleware (e.g. retrieve the inner client through `GetService`, or hold the registry directly in your decorator).
4. **`AgentRunResponse.Usage` aggregation across tool-loop iterations is implementation-defined.** For audit-grade accounting in a regulated environment, log raw per-call `ChatResponse.Usage` from your `DelegatingChatClient` rather than only the agent-level roll-up. The `FunctionInvokingChatClient`'s aggregation behaviour is not documented as part of the stable contract.
5. **Streaming `UsageContent` is best-effort.** Some providers emit no usage on streamed responses; for Anthropic's streaming path, set the SDK option that requests `streamUsage: true` (the option name varies by SDK; on the official `Anthropic` 12.x SDK it's exposed via `MessageCreateParams` extras). Always have a fallback path that recomputes input tokens from the prompt and counts output tokens locally.
6. **`AdditionalProperties` and `AdditionalCounts` keys are not contractual.** `UsageDetails.AdditionalCounts` keys (e.g. `cache_read_input_tokens` vs `CacheReadInputTokens`) differ by adapter and may change. Code defensively (case-insensitive lookup, multiple known keys), and add unit tests that assert specific provider versions.
7. **Concurrency on `ChatOptions`.** The framework documents that implementations may *mutate* the options instance. If you cache a `ChatOptions` per-mission and reuse it on multiple concurrent runs, internal mutation (e.g. `ConversationId` being assigned by a service-managed-history adapter) can race. Always `Clone()` on entry to your middleware before mutation.
8. **OpenTelemetry middleware is not a substitute for billing telemetry.** `UseOpenTelemetry()` records token counts as span attributes and meter counters but does not aggregate by mission, tenant, or session in any banking-relevant way. Use it for ops observability; build your own metered counter (e.g., `Meter("Bank.AI.Cost")` with tags for `mission_id`, `tenant_id`, `model_id`) inside your `DelegatingChatClient`.
9. **MAF has no built-in "mission" or "budget" concept.** The platform exposes `AIAgent`, `AgentSession`, `Workflow`, `WorkflowContext`, and OpenTelemetry hooks. Mission-scoped budgets, per-tenant cost ledgers, and circuit-breakers on cumulative spend are all *your* responsibility and belong in the `IChatClient` middleware layer or in a higher-level orchestration service (an APIM-style AI gateway is the pattern Microsoft itself documents in the Foundry community-hub blog).
10. **Preview surface APIs to flag.** `ResponseContinuationToken` (background responses) and parts of the workflow declarative YAML pipeline are marked `[Experimental]`; `Microsoft.Agents.AI.Workflows.Declarative` is still preview. Avoid them in the critical billing path.
11. **One reported issue you may hit.** GitHub issue `microsoft/agent-framework#1279` documents that `ChatClientAgent.RunStreamingAsync` does not always honour `ChatClientAgentRunOptions.ChatOptions` overrides when default options are also configured on the agent — relevant if you rely on per-call `ModelId` overrides for budget-driven downgrades. Verify on your target version with an integration test that asserts the actual model name returned in `ChatResponse.ModelId` matches what your middleware set.
12. **Versions cited from third-party blogs (e.g., model names like `gpt-5.4-mini`, `claude-opus-4-7`, "Microsoft Agent Framework 1.0 GA on April 3, 2026") originate from the Microsoft .NET Blog, devblogs.microsoft.com/agent-framework, techcommunity.microsoft.com, and several developer blogs.** They are consistent across multiple Microsoft sources; treat the exact stable NuGet versions as authoritative (10.5.2 / 1.5.0 visible on nuget.org), and the precise model SKU names as illustrative — verify against your provider's current listing before pinning.