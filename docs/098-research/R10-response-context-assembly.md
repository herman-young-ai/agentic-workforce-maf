# Context Assembly Pipeline on Microsoft Agent Framework (C#) — Research Brief, May 2026

## TL;DR
- **MAF is GA.** `Microsoft.Agents.AI` is at **1.5.0** (last updated 8 May 2026, GA since v1.0.0 on 3 April 2026), built on `Microsoft.Extensions.AI` **10.5.2** (stable). For a context‑assembly pipeline, the right primitives are: build a `ChatClientAgent` with `Instructions` set on `ChatClientAgentOptions`, push assembled context either by passing a pre‑built `IEnumerable<ChatMessage>` to `RunAsync(messages, session, options, ct)`, or — the framework‑idiomatic way — through one or more `AIContextProvider` instances and/or three layers of middleware (agent‑run, function, chat).
- **Token counting in .NET should standardise on `Microsoft.ML.Tokenizers` 2.0.0** (it absorbed SharpToken and DeepDev.TokenizerLib, both of which Microsoft now tells you to migrate away from). It supports Tiktoken (GPT‑3.5/4/4o/5 families), Llama, CodeGen, BPE. There is **no first‑party Microsoft tokenizer for Claude**; for Claude you must use the official Anthropic `POST /v1/messages/count_tokens` HTTP endpoint (exposed as `CountMessageTokensAsync` in the unofficial `Anthropic.SDK` 5.10.0, or via the *official* `Anthropic` 12.x NuGet released in 2025/26). Tiktoken `cl100k_base` is **not** an accurate proxy for Claude — Opus 4.7 introduced a new tokenizer that runs ~1.0–1.45× more tokens than 4.6 on the same input.
- **Storage with EF Core 10 + Npgsql 10.0.1** gives you `[Timestamp]/IsRowVersion` mapped to PostgreSQL's `xmin` system column for optimistic concurrency, JSONB via either `[Column(TypeName="jsonb")]` POCO mapping or the new EF 10 **complex‑type `ToJson()`** mapping, and — new in EF Core 10 — `ExecuteUpdateAsync` with `SetProperty` against nested JSON properties, which the Npgsql provider translates to `jsonb_set` path‑based updates. Combined with `agent.AsAIFunction()` (agent‑as‑tool) for rolling summarisation, this gives you a complete, idiomatic context‑assembly pipeline.

---

## Key Findings

| Concern | Recommended choice (May 2026) | Notes |
|---|---|---|
| Agent SDK | `Microsoft.Agents.AI` 1.5.0 | GA. Multi‑targets net8/netstandard2.0/net472. |
| AI abstractions | `Microsoft.Extensions.AI` 10.5.2 (+ `Microsoft.Extensions.AI.Abstractions` 10.5.0) | Stable since the 10.x line aligned with .NET 10 release. Major version bump from 9.x preview. |
| Token counting (GPT/Llama/local) | `Microsoft.ML.Tokenizers` 2.0.0 | `TiktokenTokenizer.CreateForModel("gpt-5"|"gpt-4o"|...)`. SharpToken (1.0.24) is in maintenance and points at this lib for migration. |
| Token counting (Claude) | Anthropic `count_tokens` API via `Anthropic` 12.20.0 (official) or `Anthropic.SDK` 5.10.0 (community) | No local tokenizer. Network call required. Free but rate‑limited. |
| EF provider | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1 (Mar 2026) | Requires EF Core ≥ 10.0.4 < 11. |
| JSON mapping | EF 10 complex types + `b.ComplexProperty(x => x.Doc, c => c.ToJson())` on PostgreSQL `jsonb` | This replaces owned‑entity JSON mapping and works with `ExecuteUpdate` path‑based updates. Legacy `[Column(TypeName="jsonb")]` POCO still works. |
| Concurrency | `[Timestamp] public uint Version { get; set; }` mapped to `xmin` | Or fluent `b.Property(x=>x.Version).IsRowVersion()`. xmin is auto‑maintained by PostgreSQL on every row write. |
| Context injection (preferred) | Subclass `Microsoft.Agents.AI.AIContextProvider`, override `InvokingAsync`/`InvokedAsync`, return `AIContext { Messages, Instructions, Tools }` | Wired through `ChatClientAgentOptions.AIContextProviders` or `AIContextProviderFactory`. |
| Per‑request override | Pass `IEnumerable<ChatMessage>` to `RunAsync(...)` and/or pass a per‑run `ChatClientAgentRunOptions(new ChatOptions { ... })` | The per‑run `ChatOptions` is merged with the agent‑level ones. |
| Middleware | Three pipeline layers: **agent‑run**, **function**, **chat (IChatClient)** | All composable via `.AsBuilder().Use(...).Build()` (chat) or `agentBuilder.Use(...)` factory (agent‑run). Order: agent‑level wraps run‑level. |
| Agent‑to‑agent | `innerAgent.AsAIFunction()` and pass it via `tools:` to the outer agent | Works locally; A2A protocol package (`Microsoft.Agents.AI.A2A`) supports out‑of‑process call. |

---

## Details

### 1. Microsoft Agent Framework (MAF) — current API surface

**Versions (8 May 2026):**
- `Microsoft.Agents.AI` 1.5.0 (last published 8 May 2026; profile shows 1.3.0 was current 24 Apr 2026 — release cadence is fast).
- `Microsoft.Extensions.AI` 10.5.2 (5 May 2026) — note the version jump from the 9.x preview line. Several MAF blog posts pin against 10.4.1, but 10.5.2 is the latest stable.
- Companion packages from the same `MicrosoftAgentFramework` NuGet profile: `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Foundry`, `Microsoft.Agents.AI.A2A`, `Microsoft.Agents.AI.A2A.AspNetCore`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Agents.AI.Workflows.Declarative`, `Microsoft.Agents.AI.Workflows.SourceGenerators`, `Microsoft.Agents.AI.DurableTask`, `Microsoft.Agents.AI.AGUI`.

GA was announced on **3 April 2026** (the Microsoft Agent Framework 1.0 blog) after a Release Candidate in February 2026. MAF supersedes Semantic Kernel and AutoGen, both of which are now in maintenance mode.

**The agent type you build:** `ChatClientAgent : AIAgent`. It wraps any `Microsoft.Extensions.AI.IChatClient` and adds tool calling, sessions, context providers, telemetry, and middleware.

**Two construction styles, both stable:**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// (a) Direct ctor — instructions are now a constructor arg (GA breaking change
//     vs preview, where instructions came via ChatClientAgentOptions.Instructions)
var agent = new ChatClientAgent(
    chatClient,
    instructions: "You are a helpful assistant.",
    name: "Assistant",
    tools: [AIFunctionFactory.Create(GetWeather)]);

// (b) Options builder — needed when you want context providers, history providers,
//     or per-request ChatOptions (Temperature, ResponseFormat, Tools, ...)
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name         = "Assistant",
    Instructions = "You are a helpful assistant.",            // GA: still here
    ChatOptions  = new ChatOptions
    {
        Temperature = 0.2f,
        Tools = [AIFunctionFactory.Create(GetWeather)]
    },
    AIContextProviders     = [new MyRagProvider(...)],
    AIContextProviderFactory = ctx => new MyMemoryProvider(ctx.SerializedState, ctx.JsonSerializerOptions),
    ChatHistoryProvider    = new InMemoryChatHistoryProvider() // or your own DB-backed
});
```

`AsAIAgent` exists as an extension on every supported provider client (`AzureOpenAIClient.GetChatClient(...)`, `OpenAIClient.GetResponseClient(...)`, `AIProjectClient`, `AnthropicClient`, etc.).

**`AIAgent.RunAsync` overloads (Microsoft.Agents.AI 1.x):**

```csharp
Task<AgentResponse> RunAsync(string message,
    AgentSession? session = null,
    AgentRunOptions? options = null,
    CancellationToken ct = default);

Task<AgentResponse> RunAsync(ChatMessage message,
    AgentSession? session = null,
    AgentRunOptions? options = null,
    CancellationToken ct = default);

// Core overload — what every other one delegates to.
Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages,
    AgentSession? session = null,
    AgentRunOptions? options = null,
    CancellationToken ct = default);

// Run with no input — use when context already lives in the session
Task<AgentResponse> RunAsync(AgentSession? session = null, ...);

// Strongly typed structured output
Task<AgentResponse<T>> RunAsync<T>(AgentSession? session = null,
    JsonSerializerOptions? serializerOptions = null,
    AgentRunOptions? options = null, CancellationToken ct = default);
```

`RunStreamingAsync` mirrors all of these and yields `AgentResponseUpdate` items; aggregate `update.Text` to assemble final output. `update.AsChatResponseUpdate()` is provided for interop with the raw MEAI streaming API.

**GA‑era breaking changes you must account for:**
1. The third positional parameter of `RunAsync` was renamed `thread` → `session` (and its type from `AgentThread` to `AgentSession`). Named‑argument call sites compile‑break. The framework still exposes `agent.GetNewThread()` / `AgentThread` types in some surface areas (the `AgentSession` is the conceptual replacement; in C# documentation and code samples published since GA, both names appear because some provider‑specific types still derive from `AgentThread`).
2. `Instructions` was removed from `ChatClientAgentOptions` and moved to the `ChatClientAgent` constructor *in some samples*, but the property is still present on `ChatClientAgentOptions` in the GA API surface and is the path that `AsAIAgent(...)` overloads route through. (Practical recommendation: set it on options when using `AsAIAgent` and via the ctor when using `new ChatClientAgent(...)` — both work, both are documented.)
3. The dependency on `Microsoft.Extensions.AI` jumped from 9.x preview to 10.x stable.

**Per‑run options:** to override `ChatOptions` for a single call (tools, temperature, response format, structured output schema), use `ChatClientAgentRunOptions`:

```csharp
var perRun = new ChatClientAgentRunOptions(new ChatOptions {
    Temperature = 0.0f,
    Tools = [AIFunctionFactory.Create(GetWeather)]
});
await agent.RunAsync("…", session, options: perRun, ct);
```

**Sessions / threads:** `AgentSession` is the abstraction for chat history + remote state references. Create with `await agent.CreateSessionAsync()`; persist with `agent.SerializeSession(session)` / `await agent.DeserializeSessionAsync(json)`. Service‑managed providers (Foundry, OpenAI Responses with `store=true`) keep history server‑side and `AgentSession` only carries an id; chat‑completion providers carry the messages locally. `AgentSession` instances are not safe to share across different agents because providers can attach session‑specific behaviour.

### Injecting assembled context — the four idiomatic patterns

For a context‑assembly pipeline these are listed roughly in increasing "MAF‑native" order:

1. **Pre‑built message list passed to `RunAsync(IEnumerable<ChatMessage>, …)`.** Simplest. You assemble `[ChatMessage(System, "<assembled context>"), ChatMessage(User, prompt)]` yourself and call `RunAsync(messages, session: null, …)`. This is exactly what the published Azure App Service multi‑agent reference solution does (`BaseAgent.InvokeAsync` calls `Agent.RunAsync(chatHistory, session: null, options: null, ct)`).

2. **`ChatClientAgentOptions.Instructions` for static system prompt** plus per‑run `ChatOptions` for any dynamic system‑level overrides.

3. **Custom `AIContextProvider` (recommended for clean separation).** Inherit `Microsoft.Agents.AI.AIContextProvider` and override:
   - `InvokingAsync(InvokingContext context, CancellationToken ct)` — return an `AIContext { Messages, Instructions, Tools }` that gets *merged* with the agent's existing instructions/messages/tools before the LLM call. This is where you inject RAG hits, episodic memory, rolling summary, scratchpad notes, etc.
   - `InvokedAsync(InvokedContext context, CancellationToken ct)` — runs after the LLM responds; this is where you extract facts to remember, update token budgets, or trigger a summarisation pass.
   - `SerializeAsync(...)` plus a `(JsonElement, JsonSerializerOptions)` constructor — to round‑trip provider state with the session.

   Wire it in via `ChatClientAgentOptions.AIContextProviders = [...]` (single instance per agent, keep it stateless and store per‑session state inside `AgentSession`) or via `AIContextProviderFactory` (one provider instance per session/thread — better when the provider needs per‑session fields).

   The framework filters input messages, calls `ProvideAIContextAsync`/`InvokingAsync`, stamps returned messages with provider source info, and merges (appends) returned `Messages`/`Tools`/`Instructions` to the existing chat input.

4. **Use `AIContextProvider` *as chat‑client middleware*** — for cross‑agent reuse. Documented pattern:
    ```csharp
    var chatClient = projectClient.AsIChatClient(deploymentName)
        .AsBuilder()
        .UseAIContextProviders(new MyContextProvider())
        .Build();
    ```

### MAF middleware (three layers, all stable in 1.0)

MAF has middleware analogous to ASP.NET Core, with three interception points:

| Layer | What it wraps | C# delegate signature |
|---|---|---|
| **Agent‑run middleware** | The whole agent turn (one user message → one final response, regardless of how many internal LLM calls or tool calls occur) | `async Task<AgentResponse>(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, AIAgent innerAgent, CancellationToken ct)` |
| **Function (tool) middleware** | Each tool invocation; only available when the underlying chat client uses `FunctionInvokingChatClient` (which `ChatClientAgent` enables by default) | Function context + `next` |
| **Chat (`IChatClient`) middleware** | Each raw model call. Fires once per turn for non‑tool responses, once per tool round‑trip for tool‑calling sequences | Wrapping `getResponseFunc`/`getStreamingResponseFunc` |

Streaming variants exist for all of them (`async IAsyncEnumerable<AgentResponseUpdate> CustomStreamingMiddleware(...)`). There's a `Use(sharedFunc:)` overload that lets a single delegate cover both modes for input‑only inspection without disabling streaming.

**Registration:**

```csharp
// Agent-run middleware via factory
var middlewareEnabledAgent = baseAgent
    .AsBuilder()
    .Use(CustomAgentRunMiddleware,             // non-streaming
         CustomAgentRunStreamingMiddleware)    // streaming
    .Build();

// Chat (IChatClient) middleware
var chatClient = providerClient.AsIChatClient(deployment)
    .AsBuilder()
    .Use(getResponseFunc: CustomChatClientMiddleware,
         getStreamingResponseFunc: null)
    .Build();
var agent = new ChatClientAgent(chatClient, instructions: "...");
```

**Pipeline ordering:** agent‑level middleware wraps run‑level wraps the agent core wraps function/chat middleware. Multiple components of the same kind chain in registration order — each must call the supplied `next` to continue, or short‑circuit by returning a synthesised `AgentResponse`.

**For your use case (context assembly before LLM call):**
- Use **agent‑run middleware** if you want to sit *outside* the inner reasoning loop — e.g., to enforce token budget on the whole turn, attach a correlation id, or audit.
- Use a **chat middleware** to mutate the actual `IList<ChatMessage>` the LLM sees (e.g., insert assembled context as an extra system message at index 0 immediately before each model call, including each tool‑result round‑trip):
  ```csharp
  static async Task<ChatResponse> InjectContextMiddleware(
      IEnumerable<ChatMessage> messages,
      ChatOptions? options,
      IChatClient inner,
      CancellationToken ct)
  {
      var assembled = await _contextAssembler.BuildAsync(_currentRequestId, ct);
      var withContext = new List<ChatMessage> { new(ChatRole.System, assembled) };
      withContext.AddRange(messages);
      return await inner.GetResponseAsync(withContext, options, ct);
  }
  ```
- Use **`AIContextProvider`** when context is conceptually agent memory (preferred; round‑trips with session state, has clear `before/after` lifecycle).

### Agent‑to‑agent invocation (rolling‑summary use case)

`AIAgent.AsAIFunction()` converts any agent into an `AIFunction` that the outer agent's LLM can call. Pass the resulting tool in `ChatOptions.Tools` or via the `tools:` parameter. Inner agent runs its own loop independently.

```csharp
// Summarisation agent
AIAgent summarizer = chatClient.AsAIAgent(new ChatClientAgentOptions {
    Name = "Summarizer",
    Description = "Compresses prior conversation into a concise rolling summary.",
    Instructions = "Given a transcript, produce a <= 400 token summary that preserves entities, decisions, open tasks."
});

// Main agent uses the summariser as a tool
AIAgent main = chatClient.AsAIAgent(new ChatClientAgentOptions {
    Instructions = "You are an assistant. When the conversation history exceeds budget, call Summarizer.",
    ChatOptions  = new ChatOptions { Tools = [summarizer.AsAIFunction()] }
});
```

For *programmatic* (non‑LLM‑mediated) invocation — which is what you usually want for a deterministic rolling‑summary policy — just call the summariser directly inside an `AIContextProvider.InvokedAsync` or inside chat middleware:

```csharp
public override async ValueTask InvokedAsync(InvokedContext ctx, CancellationToken ct)
{
    if (TokenCount(ctx.Session) > _budget)
    {
        var transcript = SerializeMessages(ctx.Session);
        var summary    = await _summarizerAgent.RunAsync(transcript, session: null, ct: ct);
        await ReplaceWithSummaryAsync(ctx.Session, summary.Text);
    }
}
```

For multi‑agent orchestration patterns beyond ad‑hoc tool composition, MAF ships `Microsoft.Agents.AI.Workflows` (sequential, fan‑out/fan‑in, group chat, handoff, magnetic) and `Microsoft.Agents.AI.DurableTask` (durable execution, checkpointing, distributed runner, observability dashboard) — relevant if your context assembly graph itself needs to survive process restarts.

### 2. Token counting in .NET (May 2026)

**`Microsoft.ML.Tokenizers` 2.0.0** is the recommended .NET tokenizer. Microsoft Learn explicitly states: *"If you're currently using DeepDev.TokenizerLib or SharpToken, consider migrating to Microsoft.ML.Tokenizers."* SharpToken's own README points at the migration guide and labels itself a community port that is now superseded.

Capabilities of `Microsoft.ML.Tokenizers` 2.0.0:
- Tiktoken (BPE for OpenAI models): `TiktokenTokenizer.CreateForModel("gpt-4")`, `"gpt-4o"`, `"gpt-5"`, `"gpt-5.4"`, etc.
- Llama tokenizer (loadable from a model file).
- CodeGen tokenizer (Phi‑2, codegen‑350M‑mono).
- BPE base class for custom vocabularies.
- API: `EncodeToIds(text)`, `EncodeToTokens(text, out normalized)`, `CountTokens(text)`, `Decode(ids)`, `GetIndexByTokenCount(text, max, out processedText, out actualCount)`, `GetIndexByTokenCountFromEnd(...)`. The two `GetIndexByTokenCount...` methods are excellent for trimming context to a hard token budget.

**No, there is no `Microsoft.Extensions.AI` tokenizer namespace.** Tokenization lives in `Microsoft.ML.Tokenizers`; MEAI consumes it where needed but does not expose its own abstraction.

**Claude / Anthropic — there is no local tokenizer:**
- Anthropic does **not** publish a client‑side tokenizer for Claude 3, 3.5, 4, 4.5, 4.6, or 4.7. The pre‑Claude‑3 tokenizer is not representative.
- The supported way to count Claude tokens is the API endpoint `POST /v1/messages/count_tokens` (free of charge, has its own RPM rate limit, accepts the same structured input as `/v1/messages` including system, tools, images, PDFs, thinking blocks).
- C# wrappers: the **official** `Anthropic` 12.x package on NuGet (released 2025/2026; versions ≤ 3.x belong to the legacy tryAGI fork now at `tryAGI.Anthropic`); and the popular community SDK `Anthropic.SDK` 5.10.0, which exposes `AnthropicClient.Messages.CountMessageTokensAsync(MessageCountTokenParameters)`.
- **Tiktoken (`cl100k_base`, etc.) is not an accurate proxy for Claude.** Vocabularies differ; the gap was small for Claude 3/3.5 but became material with **Claude Opus 4.7 (Nov 2025)**, which introduced a new tokenizer that produces ~1.0–1.35× more tokens (Anthropic's official range) and has been measured at up to ~1.47× on technical content. Treat any local estimate as ±10% on natural English and worse on code/JSON/non‑Latin scripts.
- For an offline approximation when an API call is not acceptable, the published "Counting Claude Tokens Without a Tokenizer" approach (linear regression on bytes/runes/words against ground truth from `count_tokens`) is more reliable than `cl100k_base`. There is also an unofficial `LLMSharp.Anthropic.Tokenizer` package — its data dates from 2023 and is therefore inaccurate for current Claude models; do not rely on it.

**Practical recommendation for the pipeline:** use `Microsoft.ML.Tokenizers` for OpenAI/Azure OpenAI/local models, and the Anthropic `count_tokens` HTTP endpoint (cached aggressively, batched per turn) for Claude. If you need a synchronous, offline budget guard for Claude, calibrate a linear estimator against the API for your specific content distribution and treat it as a soft upper bound (multiply by 1.1–1.5).

### 3. EF Core 10 + Npgsql for versioned JSON documents

**Versions:** EF Core 10.0.x; `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1 (12 Mar 2026), requires `Microsoft.EntityFrameworkCore` ≥ 10.0.4 < 11.

**Three ways to map a JSONB column** in the Npgsql provider:
1. **Plain `string` mapped to jsonb** (least typed, you serialise yourself).
2. **POCO with `[Column(TypeName="jsonb")]`** — the legacy Npgsql JSON mapping; `JsonDocument` is auto‑recognised. Limited LINQ translation.
3. **EF 10 complex‑type ToJson mapping (recommended for new code):**
   ```csharp
   modelBuilder.Entity<Document>(b =>
   {
       b.ComplexProperty(d => d.Body, c => c.ToJson());  // jsonb column
   });
   ```
   This is what EF 10 documents as the preferred way; it replaces both legacy POCO mapping and owned‑entity JSON mapping and supports LINQ querying as well as `ExecuteUpdateAsync`.

**Path‑based mutations on JSONB (EF Core 10):** `ExecuteUpdateAsync` now accepts nested property paths into a JSON‑mapped complex type, and the Npgsql provider translates these to `jsonb_set` / PostgreSQL JSONB path operators:

```csharp
await db.Documents
    .Where(d => d.Id == id)
    .ExecuteUpdateAsync(s => s
        .SetProperty(d => d.Body.Sections.Header.Title, newTitle)
        .SetProperty(d => d.Body.Stats.Views,           v => v + 1));
```

For arbitrary JSON paths not exposed as typed properties, drop to raw SQL with `FromSqlInterpolated` or `Database.ExecuteSqlInterpolated` using `jsonb_set(col, '{a,b,c}', to_jsonb(@val))`. The Npgsql provider also exposes `EF.Functions.JsonContains`, `JsonContained`, `JsonExists`, `JsonExistAny`, `JsonExistAll` for query‑side JSONB operations.

**Caveat (open issue Feb 2026):** EF Core 10 has a known regression in `Npgsql.EntityFrameworkCore.PostgreSQL` where `LINQ Contains` against properties mapped to `jsonb` throws during translation (`NpgsqlTypeMappingSource.FindCollectionMapping`). Pin and test if this affects your query shapes; the issue was filed Feb 2026 and may be resolved in a 10.0.x patch.

**Optimistic concurrency on PostgreSQL:** PostgreSQL has no `rowversion` type, but every row has an implicit **`xmin`** system column (the ID of the latest writing transaction). Map a `uint` property to it as a concurrency token:

```csharp
public class Document
{
    public Guid   Id      { get; set; }
    public Body   Body    { get; set; } = default!;
    [Timestamp]                          // or fluent: b.Property(d => d.Version).IsRowVersion()
    public uint   Version { get; set; }  // mapped to xmin
}
```

`IsRowVersion()` on Npgsql is automatically wired to `xmin`, no migration column is needed conceptually — though as documented in the Npgsql issue tracker, EF will currently emit an `AddColumn<uint>("xmin", type: "xid", rowVersion: true, ...)` migration; this is the documented behaviour and the column is the system column, so the migration is effectively a no‑op against the existing PG row metadata. xmin's 32‑bit space is reused after VACUUM, which is handled transparently by PostgreSQL.

On `DbUpdateConcurrencyException`, the standard EF retry pattern is: detach the stale entity (`Entry.State = Detached`), re‑read with `FindAsync`, re‑apply the user change, save again. For a context‑assembly pipeline this is exactly the right behaviour: another writer (e.g., a parallel summariser run) appended a delta you missed; reconcile and retry.

**Combining concurrency + JSON path updates:** `ExecuteUpdateAsync` bypasses the change tracker and therefore does **not** check concurrency tokens automatically. If you need concurrency on path‑based JSON mutations, either include the version in the `Where` clause and check the affected‑rows count yourself:

```csharp
var rows = await db.Documents
    .Where(d => d.Id == id && d.Version == expectedVersion)
    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Body.Notes, newNotes));
if (rows == 0) throw new DbUpdateConcurrencyException();
```

…or perform the change through tracked entity updates and `SaveChangesAsync` (slower but full change‑tracking semantics). xmin is generally not used in client‑provided where clauses because it isn't deterministic across backups; a manually maintained `int Version` with `IsConcurrencyToken()` (incremented in code or via `ExecuteUpdate`) is the conservative choice for a context‑assembly store that must survive backup/restore.

### 4. End‑to‑end pattern for the context assembly pipeline

A canonical layout that combines all of the above:

```csharp
// 1. Storage entity (EF Core 10 + Npgsql)
public class ConversationContext
{
    public Guid Id { get; set; }
    public ContextDocument Document { get; set; } = default!;   // jsonb via ToJson()
    [Timestamp] public uint Version { get; set; }                // xmin
}

// 2. Context provider — pulls assembled context per turn
internal sealed class AssembledContextProvider(
    IConversationStore store,
    AIAgent summariser,
    ITokenizer tokenizer) : AIContextProvider
{
    public override async ValueTask<AIContext> InvokingAsync(
        InvokingContext ctx, CancellationToken ct = default)
    {
        var doc = await store.LoadAsync(ctx.SessionId, ct);

        // Compose system context
        var systemBlocks = new List<ChatMessage>
        {
            new(ChatRole.System, $"<rolling-summary>{doc.RollingSummary}</rolling-summary>"),
            new(ChatRole.System, $"<facts>{string.Join("\n", doc.Facts)}</facts>"),
            new(ChatRole.System, $"<rag>{await RagHits(ctx, ct)}</rag>")
        };

        return new AIContext
        {
            Messages = systemBlocks,           // appended in front of caller messages
            // Instructions = "...",            // optional extra instructions
            // Tools = [...]                    // optional dynamic tools
        };
    }

    public override async ValueTask InvokedAsync(InvokedContext ctx, CancellationToken ct = default)
    {
        var tokens = tokenizer.CountTokens(ctx.Session);
        if (tokens > _budget)
        {
            var summary = await summariser.RunAsync(
                Serialize(ctx.Session), session: null, options: null, ct);
            await store.UpdateRollingSummaryAsync(ctx.SessionId, summary.Text, ct);
        }
        await store.AppendMessagesAsync(ctx.SessionId, ctx.RequestMessages, ctx.ResponseMessages, ct);
    }
}

// 3. Agent wiring
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name         = "MainAgent",
    Instructions = "You are a domain assistant…",
    ChatOptions  = new ChatOptions { Tools = [...] },
    AIContextProviderFactory = ctx => new AssembledContextProvider(store, summariserAgent, tokenizer)
});

// 4. Run
var session  = await agent.CreateSessionAsync();
var response = await agent.RunAsync(userPrompt, session, ct: ct);
```

If you prefer to do context assembly *inside* the model‑call layer (e.g. to inject context on every tool round‑trip during a multi‑step reasoning loop), put the same logic in **chat middleware** registered via `chatClient.AsBuilder().Use(getResponseFunc: ..., getStreamingResponseFunc: ...).Build()` and pass the wrapped client to `ChatClientAgent`.

---

## Caveats

- **Version churn.** MAF is moving fast — `Microsoft.Agents.AI` shipped 1.0 on 3 April 2026 and was already at 1.5.0 by 8 May 2026. Pin versions explicitly and recheck breaking‑change notes between minor releases. The `thread`→`session` rename, removal of preview overloads, and the 9.x→10.x MEAI bump have all happened since GA.
- **`AgentSession` vs `AgentThread` terminology.** Microsoft Learn and the GA NuGet still uses both names: `AgentSession` is the conceptual base abstraction post‑GA, but several samples (and `AIAgent.GetNewThread()`) and the `AgentThread` symbol still exist as derived/legacy types from preview. Read with care; in current C# code paths, `RunAsync(... AgentSession session ...)` is canonical.
- **`AIContextProvider` lifecycle nuance.** A single `AIContextProvider` instance is reused across all sessions of one agent, so put per‑session state inside `AgentSession` (via the `ProviderSessionState<T>` helper or your own typed state). Use `AIContextProviderFactory` if you'd rather have one instance per session.
- **`ExecuteUpdateAsync` does not run optimistic concurrency.** Always include `Version == expectedVersion` in the where‑clause when you need it.
- **JSONB Contains regression in EF Core 10 / Npgsql 10.** Open issue (#3745, Feb 2026) — `LINQ Contains` against `jsonb`-mapped properties throws during translation. Test your specific queries; if affected, fall back to `EF.Functions.JsonContains` or raw SQL.
- **Claude tokenization is unreliable offline.** The Anthropic `count_tokens` API is the only authoritative source. If you must approximate locally, calibrate per‑model — and re‑calibrate when Anthropic ships a new tokenizer (Opus 4.7 already changed the count by up to 1.47× vs 4.6).
- **MEAI version drift.** Some MAF samples published before May 2026 pin to `Microsoft.Extensions.AI` 10.4.1; the latest stable is 10.5.2. Both work with `Microsoft.Agents.AI` 1.5.0. New `IImageGenerator` interface in MEAI is still flagged experimental.
- **Workflows and durability are separate packages.** `Microsoft.Agents.AI.Workflows` (graph orchestration), `Microsoft.Agents.AI.Workflows.Declarative` (declarative DSL), `Microsoft.Agents.AI.Workflows.SourceGenerators` (compile‑time route validation), and `Microsoft.Agents.AI.DurableTask` (Durable Task–backed runtime) are layered on top of the core. You don't need any of them to ship a single‑agent context‑assembly pipeline; reach for them only when the assembly graph itself becomes long‑running or distributed.
- **Speculative / forward‑looking content flagged.** Some community write‑ups speculate about API changes "before GA" — those notes are stale (GA shipped on 3 April 2026). Where the official Microsoft Learn API reference and the GA blog disagree with older preview blog posts, trust the API reference and the GA notes.