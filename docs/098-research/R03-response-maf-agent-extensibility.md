# Microsoft Agent Framework (MAF) for .NET / C# — Technical Reference (May 2026)

## TL;DR

- **Microsoft Agent Framework hit 1.0 GA on April 3, 2026** under the package family **`Microsoft.Agents.AI`** (namespace `Microsoft.Agents.AI`). The earlier names "Microsoft.Extensions.AI.Agents" / "Microsoft.Extensions.Agents" / "Semantic Kernel Agents" were never the GA names — they were superseded. The base type is `public abstract class AIAgent` in `Microsoft.Agents.AI.Abstractions.dll`; the default LLM-backed implementation is `public sealed class ChatClientAgent : AIAgent`, built around `Microsoft.Extensions.AI.IChatClient`. Subclassing `AIAgent` (overriding `RunCoreAsync`, `RunStreamingCoreAsync`, `CreateSessionCoreAsync`, `DeserializeSessionCoreAsync`) is the supported extensibility point; `ChatClientAgent` itself is `sealed`, so wrap with `DelegatingAIAgent` or middleware for pre/post logic.
- **State, context, prompts, and tools are first-class GA features.** `AgentSession` (the replacement/rename of the old `AgentThread`) carries conversation state and a typed state bag accessed via `ProviderSessionState<T>` with a `StateKey`. System prompts are configured as a single string `Instructions` (on the constructor or via `ChatClientAgentOptions.Instructions` / `ChatOptions.Instructions`); composition of multiple prompt layers happens through one or more `AIContextProvider` instances that return an `AIContext { Instructions, Messages, Tools }` per turn. Tools are `Microsoft.Extensions.AI.AIFunction`/`AITool` instances created with `AIFunctionFactory.Create(...)`. `agent.AsAIFunction()` converts any `AIAgent` into a tool callable by another agent. A three-layer middleware pipeline (agent, function, chat) is exposed via `agent.AsBuilder().Use(...).Build()` and is the canonical place for budget tracking, cost recording, timeouts, logging, and content filters.
- **Provider-agnostic via `IChatClient`.** Anthropic Claude, OpenAI, Azure OpenAI, Microsoft Foundry, GitHub Models, Amazon Bedrock, Google Gemini, and Ollama are all supported in 1.0; swapping providers is a one-line change because every provider exposes an `IChatClient` (or an `AsIChatClient()`/`AsAIAgent()` extension). Core packages (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`) are GA; several adjacent packages — notably `Microsoft.Agents.AI.Anthropic`, `Microsoft.Agents.AI.Foundry`, `Microsoft.Agents.AI.A2A`, `Microsoft.Agents.AI.Hosting.*`, `Microsoft.Agents.AI.Workflows.Declarative`, and `Microsoft.Agents.AI.DurableTask` — were still being installed with `--prerelease` flags as of May 2026, so pin versions explicitly.

---

## Key Findings

### 1. Identity, naming, and lifecycle
- **Official name (May 2026):** Microsoft Agent Framework (MAF). Successor to Semantic Kernel + AutoGen.
- **GA milestone:** version 1.0 announced on devblogs.microsoft.com/agent-framework on **3 April 2026**. Release Candidate landed February 2026.
- **Root namespace:** `Microsoft.Agents.AI` (not `Microsoft.Extensions.AI.Agents`, despite an earlier proposal in GitHub issue #451). Builds on top of `Microsoft.Extensions.AI` ("MEAI").
- **Source repo:** `github.com/microsoft/agent-framework` (not `microsoft/agents` or `microsoft/semantic-kernel`).

### 2. NuGet packages (May 2026)
| Package | Status | Latest published version observed |
|---|---|---|
| `Microsoft.Agents.AI` | GA (1.0 LTS, April 2026) | 1.5.0 (core; ships beyond 1.0 minor releases) |
| `Microsoft.Agents.AI.Abstractions` | GA, with ongoing `1.0.0-rcN`/preview drops for new abstractions | 1.0.0-rc2 visible on `AIAgent` API page |
| `Microsoft.Agents.AI.OpenAI` | GA | 1.0.0-rc1 / 1.0.0-preview.260128.1 (preview channel for newer features) |
| `Microsoft.Agents.AI.AzureAI` | Preview | 1.0.0-preview.260205.1 |
| `Microsoft.Agents.AI.Foundry` | Preview (`--prerelease`) | 1.0.0-preview.x |
| `Microsoft.Agents.AI.Anthropic` | Preview (`--prerelease`) | newer than the OpenAI provider; *partial parity*: function tools work, but code interpreters / hosted MCP / web search / file search are not yet supported |
| `Microsoft.Agents.AI.A2A` | Preview | A2A v1 SDK protocol |
| `Microsoft.Agents.AI.Hosting` | Preview | DI / `AddAIAgent(...)` extensions for `IHostApplicationBuilder` |
| `Microsoft.Agents.AI.Hosting.A2A` / `.A2A.AspNetCore` | Preview | Server-side A2A endpoints (`builder.AddA2AServer(...)`, `app.MapA2AHttpJson(...)`) |
| `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` | Preview | AG-UI SSE protocol (`builder.Services.AddAGUI()`, `app.MapAGUI("/", agent)`) |
| `Microsoft.Agents.AI.Hosting.OpenAI` | Alpha | 1.0.0-alpha.x |
| `Microsoft.Agents.AI.Workflows` | GA | Imperative graph workflows |
| `Microsoft.Agents.AI.Workflows.Declarative` | Preview | YAML/declarative workflows |
| `Microsoft.Agents.AI.DurableTask` | Preview | Durable workflows on Durable Task |
| `Microsoft.Agents.AI.Hyperlight` | New / experimental | CodeAct integration |
| `Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.Abstractions` | GA | 10.x (10.5.2 observed); MAF takes a `>= 10.2.0` dependency |

The legacy `Microsoft.Agents.Extensions.Teams.AI` (1.1.x – 1.3.x beta) is a *different* product (Teams AI Library on the M365 Agents SDK), not MAF. Don't confuse the two when picking package names.

Compatible TFMs for MAF: **.NET 8.0 / .NET Standard 2.0 / .NET Framework 4.7.2** (per the NuGet metadata).

### 3. The `AIAgent` base class
Namespace `Microsoft.Agents.AI`, assembly `Microsoft.Agents.AI.Abstractions.dll`.

```csharp
public abstract class AIAgent
{
    // Identity
    public string Id { get; }                         // unique per agent instance
    protected virtual string IdCore { get; }          // override to provide a custom Id
    public string? Name { get; }                      // human-readable name
    public string? Description { get; }               // agent purpose/capabilities

    // Per-run ambient context (used by middleware / context providers)
    public AgentRunContext? CurrentRunContext { get; set; }

    // Public entry points (non-virtual; they delegate to *Core* methods)
    public Task<AgentResponse> RunAsync(string message,
        AgentSession? session = null, AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    public Task<AgentResponse> RunAsync(ChatMessage message, AgentSession? session = null, ...);
    public Task<AgentResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, ...);
    public Task<AgentResponse> RunAsync(AgentSession session, AgentRunOptions? options = null, ...); // no new input
    public Task<TResult> RunAsync<TResult>(...); // structured output overload

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(...); // streaming overloads

    // Sessions
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default);
    public ValueTask<AgentSession> DeserializeSessionAsync(JsonElement state,
        JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken ct = default);
    public JsonElement SerializeSession(AgentSession session, JsonSerializerOptions? jso = null);

    // Service / metadata access (M.E.AI-style escape hatch)
    public virtual object? GetService(Type serviceType, object? key = null);
    public TService? GetService<TService>(object? key = null);

    // Override points for custom subclasses
    protected abstract Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken);

    protected abstract IAsyncEnumerable<AgentResponseUpdate> RunStreamingCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken);

    protected virtual ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken ct);
    protected virtual ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement state, JsonSerializerOptions? jso, CancellationToken ct);
}
```

**Built-in derived types:**
- `ChatClientAgent` — `IChatClient`-backed, the workhorse type. **`sealed`.**
- `A2AAgent` — wraps a remote Agent-to-Agent endpoint.
- `CopilotStudioAgent` — Copilot Studio bridge.
- `DelegatingAIAgent` — base for cleanly wrapping/decorating another `AIAgent`.
- `DurableAIAgent` — Durable Task–backed variant for long-running workflows.

**To inject pre/post logic** you have three supported choices, in increasing order of effort:

1. **Middleware** (recommended for cross-cutting concerns):
   ```csharp
   AIAgent wrapped = inner.AsBuilder()
       .Use(runFunc: MyAgentRunMiddleware,
            runStreamingFunc: MyAgentRunStreamingMiddleware)
       .Build();
   ```
2. **`DelegatingAIAgent`** — derive your own class, override only the run methods you care about, delegate the rest to an `InnerAgent`.
3. **Subclass `AIAgent` directly** — required for fully custom agents (no `IChatClient`); override `RunCoreAsync`, `RunStreamingCoreAsync`, `CreateSessionCoreAsync`, `DeserializeSessionCoreAsync`. The Microsoft Learn "Custom Agents" walkthrough builds an `UpperCaseParrotAgent : AIAgent` this way and pairs it with `InMemoryAgentSession`.

> ⚠️ Because `ChatClientAgent` is **sealed**, you cannot inherit from it. To override its execution, either wrap it via `AsBuilder().Use(...)`, decorate the underlying `IChatClient` (which is itself a delegating-pipeline pattern from MEAI), or implement `DelegatingAIAgent`/your own `AIAgent` subclass.

### 4. `ChatClientAgent` and construction patterns
```csharp
public sealed class ChatClientAgent : AIAgent
{
    // Convenience constructor
    public ChatClientAgent(
        IChatClient chatClient,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IEnumerable<AITool>? tools = null);

    // Full constructor
    public ChatClientAgent(IChatClient chatClient, ChatClientAgentOptions options,
                           ILoggerFactory? loggerFactory = null);
}
```

`ChatClientAgentOptions` (the canonical builder/options surface) — observed properties:

| Property | Type | Purpose |
|---|---|---|
| `Id` | `string?` | Stable agent id (used as DI key, telemetry attribute) |
| `Name` | `string?` | Display name (becomes the tool name when used via `AsAIFunction`) |
| `Description` | `string?` | Description (becomes the tool description / agent card description) |
| `Instructions` | `string?` | System prompt text (a single string) |
| `ChatOptions` | `Microsoft.Extensions.AI.ChatOptions` | Per-call defaults: `Tools` (List<AITool>), `Temperature`, `MaxOutputTokens`, `ResponseFormat`, `ModelId`, `Tools`, `RawRepresentationFactory`, `Instructions`, etc. |
| `AIContextProviders` | `IList<AIContextProvider>` (planned multi-provider support) | Read/write hooks executed every turn. Today the .NET surface still effectively expects one provider — see GitHub issue #2933 and the upcoming `AggregateAIContextProvider`. |
| `ChatHistoryProvider` | `ChatHistoryProvider` (e.g. `InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions { ChatReducer = new MessageCountingChatReducer(20) })`) | Local conversation history strategy and reducers |
| `ChatMessageStoreFactory` | factory delegate | Custom store for messages |
| `UseProvidedChatClientAsIs` | `bool` | If `true`, suppresses the automatic `FunctionInvokingChatClient` wrapper |
| `RequirePerServiceCallChatHistoryPersistence` | `bool` | Persists history per service call inside the function-calling loop |
| `LoggerFactory` | `ILoggerFactory?` | Logging |

**Builder/factory entry points:**

```csharp
// 1. Direct constructor
var agent = new ChatClientAgent(chatClient, instructions: "You are helpful.");

// 2. IChatClient extension (the most common pattern)
AIAgent agent = chatClient.CreateAIAgent(
    new ChatClientAgentOptions { Name = "Joker", Instructions = "...", ChatOptions = new ChatOptions { Tools = [...] } });

// 3. Provider SDK extensions (forward to ChatClientAgent under the hood)
AIAgent a = new AzureOpenAIClient(uri, cred).GetChatClient(deployment).AsAIAgent(...);
AIAgent b = new OpenAIClient(key).GetChatClient(model).AsAIAgent(instructions: "...", tools: [...]);
AIAgent c = new AIProjectClient(uri, cred).AsAIAgent(model: "gpt-5.4-mini", instructions: "...", name: "...");
AIAgent d = new AnthropicClient { ApiKey = key }.AsAIAgent(model: "claude-sonnet-4-5-20250929", instructions: "...");
AIAgent e = new AnthropicFoundryClient(new AnthropicFoundryApiKeyCredentials(key, resource))
                .AsAIAgent(model: deploymentName, instructions: "...");

// 4. Hosting / DI
builder.AddAIAgent("pirate", instructions: "You are a pirate.", description: "...");
builder.AddAIAgent("chat", (sp, key) => new ChatClientAgent(sp.GetRequiredService<IChatClient>(),
                                                            instructions: "...", name: key));
```

Switching provider at runtime is a one-line change because every provider returns an `IChatClient`; with `[FromKeyedServices(name)]`/keyed DI you can register multiple `IChatClient` and resolve per request.

### 5. Prompt composition
- The base prompt is **a single string** (`Instructions`), set once at agent construction. It is *not* a list of system messages.
- **Layering** is achieved via `AIContextProvider` implementations. Each provider's `ProvideAIContextAsync(...)` returns an `AIContext` with three contributions:
  ```csharp
  public sealed class AIContext
  {
      public string? Instructions { get; init; }
      public IEnumerable<ChatMessage>? Messages { get; init; }
      public IEnumerable<AITool>? Tools { get; init; }
  }
  ```
  Returned `Instructions` are **appended** to the agent-level instructions; messages and tools are **appended** to those already on the run. Each context provider's contributions are stamped with source metadata (`AgentRequestMessageSourceType.AIContextProvider`) so middleware/telemetry can tell where they came from.
- A run-level override is also possible by passing `ChatClientAgentRunOptions(new ChatOptions { Instructions = "...", Tools = [...], Temperature = 0.3 })` to `RunAsync(... options: ...)` — this **merges with** (and per-run wins over) the agent defaults.
- For full multi-system-message control today, you can supply additional system messages through `AIContext.Messages` (with `ChatRole.System`) returned from a provider, since the simple `Instructions` field is a single string.

### 6. `AgentSession` and state
`AgentSession` is the GA name (the older `AgentThread` name persists in some blogs and earlier C# APIs but the current Microsoft Learn surface and `RunAsync` signatures use `AgentSession`). It is an **abstract base class**.

Key facts:
- Created **only** via `agent.CreateSessionAsync()` or `agent.DeserializeSessionAsync(json)` so the agent can attach behaviors/state-keys it owns. Do not reuse a session across different agents unless you understand the underlying threading model.
- Agents are stateless; all conversation state lives on the session.
- Session state can include: chat history (in-memory or service-managed via a `ConversationId`), references to remote memory containers, custom typed state added by `AIContextProvider`s, and any per-conversation metadata.
- Concrete subclass for the default agent: `ChatClientAgentSession` (exposes `ConversationId` for service-managed history scenarios — Foundry, OpenAI Responses).
- **Typed state bag:** the framework provides `ProviderSessionState<TState>` keyed by `StateKey` for context providers to safely store typed per-session state without colliding:
  ```csharp
  internal class MyState { public string? MemoryId { get; set; } }
  var helper = new ProviderSessionState<MyState>(
      stateInitializer: _ => new MyState { MemoryId = Guid.NewGuid().ToString() },
      stateKey: nameof(MyContextProvider));
  // Inside InvokingCoreAsync / InvokedCoreAsync:
  var state = helper.GetOrInitializeState(context.Session);
  ```
  `ChatClientAgent`'s constructor validates that all providers/history providers use unique `StateKey`s and throws if they collide.
- **Serialization:** `JsonElement json = agent.SerializeSession(session)` and round-trip via `await agent.DeserializeSessionAsync(json)`. Older blogs show `thread.Serialize()` / `AgentThread.Deserialize(...)`; in 1.0 GA the supported surface routes through the agent.
- **Hosting:** `AgentThreadStore`/`AgentSessionStore` interfaces (with `InMemoryAgentSessionStore`, `NoopAgentThreadStore`) are provided in `Microsoft.Agents.AI.Hosting` for stateless web hosts (ASP.NET Core, Azure Functions). Replace with a durable store (e.g. Cosmos DB, Redis) for production.

### 7. Tools and tool registration
- Tools are MEAI's `Microsoft.Extensions.AI.AITool` and `AIFunction` (`AIFunction : AITool`). They are registered through `ChatOptions.Tools` (`IList<AITool>`) and/or the `tools:` parameter on the agent factory methods.
- Build tools from C# methods:
  ```csharp
  AIFunction f = AIFunctionFactory.Create(GetWeather);                  // by delegate
  AIFunction g = AIFunctionFactory.Create((string loc) => "...", "get_weather", "Gets weather"); // inline
  AIFunction h = AIFunctionFactory.Create(myService.GetWeatherAsync);   // from instance method
  ```
  Reflection picks up `[Description]` attributes on the method and parameters to build the JSON schema automatically.
- `FunctionInvokingChatClient` (from MEAI) is **automatically inserted** into the pipeline when an agent receives tools — set `ChatClientAgentOptions.UseProvidedChatClientAsIs = true` to opt out.
- Other tool kinds (mostly OpenAI/Foundry-backed): code interpreter, hosted MCP, local MCP, file search, image generation, web search, computer use. Anthropic provider currently supports only function tools.
- **Agent-as-tool:** `agent.AsAIFunction()` (extension on any `AIAgent`) returns an `AIFunction` wrapper. The agent's `Name` becomes the tool name and `Description` becomes the tool description. The wrapped agent maintains its own session internally per call:
  ```csharp
  AIAgent weatherAgent = ...; // inner, with its own tools / instructions
  AIAgent main = chatClient.AsAIAgent(
      instructions: "You are a helpful assistant.",
      tools: [weatherAgent.AsAIFunction()]);
  ```
  The same trick exposes an MAF agent over MCP: `McpServerTool.Create(agent.AsAIFunction())`.

### 8. Multi-agent orchestration
Two layers:
- **Composition via tools** — `AsAIFunction()` (above) plus `A2AAgent` for cross-process/cross-framework agents over the A2A v1 protocol.
- **Workflows (`Microsoft.Agents.AI.Workflows`)** — graph-based executors and edges. Built-in patterns: sequential, concurrent (fan-out/fan-in), group chat (with selectors / orchestrator agents / `RoundRobinGroupChatManager`), handoff, magentic-one. APIs include `AgentWorkflowBuilder.BuildSequential(...)`, `WorkflowBuilder.AddEdge(...)`, `AddFanOutEdge`, `AddFanInBarrierEdge`, `workflow.AsAgentAsync()` (a workflow can be exposed as a single `AIAgent`). Each agent in a workflow gets its own `AgentSession` by default; for shared sessions configure agents with a common provider before adding them. Stateful executors that are reused must implement `IResettableExecutor.ResetAsync()`. For durability, switch to `Microsoft.Agents.AI.DurableTask` (still preview, May 2026).

### 9. `IChatClient` integration
- Every MAF agent that talks to an LLM does so through `Microsoft.Extensions.AI.IChatClient` (10.x).
- Providers ship their own NuGet (Microsoft.Agents.AI.OpenAI, .Anthropic, .Foundry, .AzureAI, …) and an `AsIChatClient()` / `AsAIAgent()` extension; community providers (e.g. OllamaSharp.OllamaApiClient) implement `IChatClient` directly and plug straight in.
- The MEAI builder is the canonical place to add IChatClient-level concerns:
  ```csharp
  IChatClient client = innerClient
      .AsBuilder()
      .UseFunctionInvocation()
      .UseOpenTelemetry(sourceName: "Agents")
      .UseLogging()
      .ConfigureOptions(o => o.RawRepresentationFactory = _ => new ChatCompletionOptions { ReasoningEffortLevel = "minimal" })
      .Build();
  ```
- **Runtime provider swap** is supported in two idiomatic ways: (a) keyed DI (`AddKeyedSingleton<IChatClient>("anthropic", ...)` + `[FromKeyedServices("anthropic")]`); (b) build separate `AIAgent` instances and select one per request. There is no built-in "switch provider mid-session" feature — switch at the `IChatClient` registration boundary instead.

### 10. Middleware pipeline
The `ChatClientAgent` pipeline has three middleware layers, registered through different APIs:

```
[Caller] → Agent middleware → ChatClientAgent core
                                ├── AIContextProvider.InvokingCoreAsync (read-path)
                                ├── ChatHistoryProvider.LoadAsync
                                ├── IChatClient pipeline
                                │     ├── Chat middleware (request/response)
                                │     ├── FunctionInvokingChatClient
                                │     │     └── Function middleware (tool invocations)
                                │     └── RawChatClient (provider call)
                                └── AIContextProvider.InvokedCoreAsync (write-path)
```

| Layer | What it sees | Registration |
|---|---|---|
| **Agent middleware** | Whole agent run (`IEnumerable<ChatMessage>`, `AgentSession?`, `AgentRunOptions?`, returns `AgentResponse` / streaming updates). Works on **any `AIAgent`** including `A2AAgent`, `CopilotStudioAgent`. Best for budgets, auth, audit, response transforms. | `agent.AsBuilder().Use(runFunc: ..., runStreamingFunc: ...).Build()` — provide *both* funcs or streaming silently degrades to non-streaming. |
| **Function (tool-call) middleware** | Each tool invocation (`FunctionInvocationContext` with `Function`, `Arguments`, settable `Terminate`). Only available where `FunctionInvokingChatClient` is in play (e.g. `ChatClientAgent`). | `agent.AsBuilder().Use(funcInvocationFunc).Build()` |
| **Chat (model-call) middleware** | Each underlying `GetResponseAsync` to the provider. Use for request/response inspection, caching, content safety, token counting. | `chatClient.AsBuilder().Use(chatMiddlewareFunc).Build()` *before* constructing the `ChatClientAgent`. |

Signatures (.NET):
```csharp
async Task<AgentResponse> MyAgentRunMiddleware(
    IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options,
    AIAgent innerAgent, CancellationToken ct) { /* pre */
    var resp = await innerAgent.RunAsync(messages, session, options, ct);
    /* post */ return resp; }

async ValueTask<object?> MyFunctionMiddleware(
    AIAgent agent, FunctionInvocationContext context,
    Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct)
{
    if (context.Function.Name == "DangerousOp") context.Terminate = true;
    return await next(context, ct);
}
```

This is the canonical place for **budget tracking, cost/token recording, timeout enforcement**, security screening, and policy. Microsoft's own `UseOpenTelemetry()` and the community `UseGovernance(...)` (Agent Governance Toolkit) plug in here.

### 11. Hosting & DI helpers
- `Microsoft.Agents.AI.Hosting` provides `IHostApplicationBuilder` extensions:
  ```csharp
  builder.AddAIAgent("name", instructions: "...");
  builder.AddAIAgent("name", instructions, chatClient);
  builder.AddAIAgent("name", instructions, description, chatClientServiceKey: "openai");
  builder.AddAIAgent("name", (IServiceProvider sp, string key) => new ChatClientAgent(...));
  ```
  Agents are registered as **keyed services**, resolved by `[FromKeyedServices("name")] AIAgent`.
- For protocol exposure: `builder.Services.AddA2AServer()` + `app.MapA2AServer()`, `builder.AddA2AServer("name")` + `app.MapA2AHttpJson("name", "/a2a/name")`, and `builder.Services.AddAGUI()` + `app.MapAGUI("/", agent)` for the AG-UI streaming protocol.

### 12. Anthropic-specific notes (May 2026)
- Package: `Microsoft.Agents.AI.Anthropic` (`--prerelease`). Direct API and Foundry-hosted variants are exposed via `AnthropicClient` and `AnthropicFoundryClient` (`Anthropic.Foundry` package adds the credential type). Bedrock/Vertex variants are Python-only as of May 2026.
- Construction:
  ```csharp
  var client = new AnthropicClient { ApiKey = apiKey };
  AIAgent agent = client.AsAIAgent(model: "claude-haiku-4-5",
      instructions: "...", name: "...");
  ```
- **Feature gaps vs. OpenAI provider:** function tools work; **no** support yet for code interpreter, hosted MCP / local MCP through the provider, web search, file search, or tool-approval surfaces. If you need those, either drop down to the Anthropic .NET SDK directly, or surface external tools as MEAI `AIFunction` wrappers around HTTP calls.

---

## Details / Code Recipes

### Minimum viable agent (Azure OpenAI)
```csharp
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deployment)
    .AsAIAgent(instructions: "You are a helpful assistant.", name: "Helper");

AgentResponse r = await agent.RunAsync("Hello!");
Console.WriteLine(r.Text);
```

### Multi-turn with serializable session
```csharp
AgentSession session = await agent.CreateSessionAsync();
await agent.RunAsync("My name is Alice.", session);
JsonElement state = agent.SerializeSession(session);
File.WriteAllText("session.json", state.GetRawText());
// later…
AgentSession resumed = await agent.DeserializeSessionAsync(JsonDocument.Parse(File.ReadAllText("session.json")).RootElement);
await agent.RunAsync("What is my name?", resumed);
```

### Custom context provider with typed state
```csharp
internal sealed class UserMemory : AIContextProvider
{
    private readonly ProviderSessionState<UserState> _state =
        new(_ => new UserState(), nameof(UserMemory));

    public override string StateKey => _state.StateKey;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken ct)
    {
        var s = _state.GetOrInitializeState(context.Session);
        var msgs = s.Name is null
            ? Enumerable.Empty<ChatMessage>()
            : new[] { new ChatMessage(ChatRole.System, $"Address the user as {s.Name}.") };
        return new(new AIContext { Messages = msgs });
    }

    protected override ValueTask StoreAIContextAsync(StoringContext context, CancellationToken ct)
    {
        // inspect context.RequestMessages / context.ResponseMessages and update _state
        return default;
    }
}
```
Wire it via `ChatClientAgentOptions.AIContextProviders = [ new UserMemory() ]`. (Multi-provider parity with Python is tracked in GitHub issue #2933 — until the `AggregateAIContextProvider` lands in .NET, design around a single composite provider per agent.)

### Custom `AIAgent` subclass (no `IChatClient`)
```csharp
internal sealed class UpperCaseAgent : AIAgent
{
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session,
        AgentRunOptions? options, CancellationToken ct)
    {
        var output = string.Concat(messages.Select(m => m.Text)).ToUpperInvariant();
        return new AgentResponse(new[] { new ChatMessage(ChatRole.Assistant, output) });
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunStreamingCoreAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session,
        AgentRunOptions? options, [EnumeratorCancellation] CancellationToken ct)
    { /* … */ }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken ct)
        => new(new InMemoryAgentSession());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement state, JsonSerializerOptions? jso, CancellationToken ct)
        => new(new InMemoryAgentSession(state, jso));
}
```

### Budget / cost / timeout middleware
```csharp
AIAgent withGuards = inner.AsBuilder()
    .Use(runFunc: async (msgs, ses, opts, next, ct) =>
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));            // timeout
        var sw = Stopwatch.StartNew();
        var resp = await next.RunAsync(msgs, ses, opts, cts.Token);
        Telemetry.Record("agent.run.ms", sw.ElapsedMilliseconds);
        Telemetry.Record("agent.tokens.in",  resp.Usage?.InputTokenCount  ?? 0);
        Telemetry.Record("agent.tokens.out", resp.Usage?.OutputTokenCount ?? 0);
        return resp;
    })
    .Use(funcInvocationFunc: async (agent, ctx, next, ct) =>
    {
        // tool budget: cap to 20 calls per session via state on the session
        if (++Counter(ctx) > 20) ctx.Terminate = true;
        return await next(ctx, ct);
    })
    .Build();
```

### Agent as a tool / sub-agent
```csharp
AIAgent weather = chatClient.AsAIAgent(
    instructions: "Answer weather questions.", name: "WeatherAgent",
    description: "An agent that answers weather questions.",
    tools: [AIFunctionFactory.Create(GetWeather)]);

AIAgent main = chatClient.AsAIAgent(
    instructions: "You are a helpful assistant.",
    tools: [weather.AsAIFunction()]);   // sub-agent maintains its own session per invocation
```

### Structured output
```csharp
AgentRunOptions opts = new() {
    ResponseFormat = ChatResponseFormat.ForJsonSchema<PersonInfo>()
};
AgentResponse r = await agent.RunAsync("Tell me about Alice.", options: opts);
PersonInfo p = JsonSerializer.Deserialize<PersonInfo>(r.Text)!;
// or: PersonInfo p = await agent.RunAsync<PersonInfo>("Tell me about Alice.");
```

### ASP.NET Core hosting + A2A
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddAIAgent("pirate",
    instructions: "You are a pirate. Speak like a pirate.",
    description: "A pirate-speaking agent.");
builder.AddA2AServer("pirate");
var app = builder.Build();
app.MapA2AHttpJson("pirate", "/a2a/pirate");
app.Run();
```

---

## Caveats / What MAF Does *Not* Do (May 2026)

1. **`ChatClientAgent` is `sealed`.** You cannot subclass it. Use `DelegatingAIAgent`, agent middleware, or your own `AIAgent` subclass.
2. **`Instructions` is a single string** at the agent and `ChatOptions` levels. There is no first-class "list of system messages" surface. To compose multiple system layers, return additional `ChatRole.System` messages from an `AIContextProvider`.
3. **Multiple `AIContextProvider`s in .NET are not yet auto-aggregated** the way Python's `AggregateContextProvider` does. Track issue #2933 / the planned `AggregateAIContextProvider`. Until shipped, write a composite provider yourself.
4. **`AIAgent` is immutable after construction.** You cannot add/remove tools or change instructions on a built agent. The idiomatic fix is the *delegating outer agent* pattern: a stable wrapper that delegates to an inner agent which is rebuilt when configuration changes (e.g. when an MCP server comes online).
5. **Many provider/hosting packages are still preview** (`--prerelease`) at GA: `Microsoft.Agents.AI.Anthropic`, `.Foundry`, `.A2A`, all `Hosting.*`, `Workflows.Declarative`, `DurableTask`, `Hosting.OpenAI` (alpha). Pin versions and watch breaking-change notes.
6. **Anthropic provider parity gap.** Function tools work, but code interpreter, hosted/local MCP through the provider, file search, web search and tool-approval flows are *not* supported as of May 2026. Use HTTP-backed `AIFunction`s as a workaround.
7. **AgentThread vs. AgentSession naming.** Some Microsoft Learn pages and many community blogs still say `AgentThread`. The current/GA C# surface uses **`AgentSession`** (the `AIAgent.RunAsync` parameter type, the `CreateSessionAsync` method, the `ChatClientAgentSession` concrete type). Treat older "thread" content as historical.
8. **Built-in stores are dev-only.** `InMemoryAgentSessionStore`, `InMemoryTaskStore`, `NoopAgentThreadStore` are explicitly intended for development. For production use a durable session store (Cosmos DB, Redis, SQL) and register it as a keyed singleton **before** calling `AddA2AServer(...)` or equivalent.
9. **No built-in cost-budget enforcer.** Token usage is exposed (`AgentResponse.Usage`, OpenTelemetry `gen_ai.usage.input_tokens`/`gen_ai.usage.output_tokens`), but you must implement budget caps yourself in agent middleware.
10. **No built-in distributed timeout/policy engine.** Timeouts are per-`CancellationToken`; resilience (retry, circuit breaker) is your responsibility — typically through Polly inserted as `IChatClient` middleware (`UsePolicy(...)`) or the agent middleware layer.
11. **Workflows + agent state isolation.** When sharing a workflow builder across runs, agent threads/sessions persist across runs in the same `Workflow` instance. Use factory functions in the builder to ensure isolated state per request, and implement `IResettableExecutor.ResetAsync()` for stateful executors.
12. **Some abstractions still ship as `1.0.0-rcN`/preview** (`Microsoft.Agents.AI.Abstractions` was observed at `1.0.0-rc2` on the public API page) even after the core `Microsoft.Agents.AI` package went GA at 1.0 (with subsequent 1.x minor releases observed up to 1.5.0 on NuGet). When generating code, target the GA core types and pin abstractions explicitly.
13. **Pre-existing legacy package names to avoid:** `Microsoft.Agents.Extensions.Teams.AI` (Teams AI Library), `Microsoft.SemanticKernel.Agents.*` (Semantic Kernel — predecessor, still maintained but not the same API), `Microsoft.AutoGen.Agents.*` (AutoGen — predecessor, still maintained but different API). MAF is **`Microsoft.Agents.AI.*`**.