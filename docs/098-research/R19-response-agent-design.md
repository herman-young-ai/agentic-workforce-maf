# Agent Model Design Reference — Microsoft Agent Framework 1.5.0 on Azure Container Apps for Investec/Icarus

**TL;DR**
- Build every agent as a `Microsoft.Agents.AI.ChatClientAgent` (a `sealed` class wrapping an `IChatClient`) constructed from a single shared `IChatClient` pipeline composed with `DelegatingChatClient` middleware (`UseOpenTelemetry → UseFunctionInvocation → UseDistributedCache`), with per-agent tool scoping done via `ChatClientAgentOptions.ChatOptions.Tools` populated from `AIFunctionFactory.Create(...)` plus optionally `mcpClient.ListToolsAsync()` from the official `ModelContextProtocol` C# SDK v1.0; persist the agent catalog in PostgreSQL via Npgsql/EF Core 10 using `jsonb` columns (complex types or `HasConversion`) for tool manifests, file scopes, and interface contracts.
- The 1.5.0 wire (Microsoft.Agents.AI 1.5.0 GA, published 2026-05-08, pinned to Microsoft.Extensions.AI 10.5.1) is API-stable vs. 1.0 GA — no breaking changes to `ChatClientAgent`/`ChatClientAgentOptions`/`AgentSession`/`AIContextProvider`/`AddAIAgent` were called out in the 1.1–1.5 release notes. The only `[Breaking]` item between 1.0 and 1.5 was `string[]` arguments for file-based skill scripts (PR #5475 in 1.4.0). New since 1.0: Magentic Orchestration (Experimental, 1.5.0), WebBrowsingTool allow-listing, message filtering, AGUI reasoning events, Handoff Orchestration refactor with HITL (1.2.0), Hyperlight CodeAct package (1.4.0).
- For regulated execution, run untrusted/LLM-generated code in Azure Container Apps **Dynamic Sessions** (Hyper-V isolated, `https://<region>.dynamicsessions.io/...`, audience `https://dynamicsessions.io`) called from an `AIFunction` tool that authenticates via Managed Identity (token-bearing identity belonging to the `Azure ContainerApps Session Executor` and `Contributor` roles on the session pool, per Microsoft Learn), uses **one session identifier per agent conversation**, and enforces file scope by mounting only allow-listed paths under `/mnt/data` per upload.

---

## Key Findings

### 1. NuGet topology at May 2026

| Package | Version | Status | Notes |
|---|---|---|---|
| `Microsoft.Agents.AI` | **1.5.0** | GA / stable | Core: `AIAgent`, `ChatClientAgent`, `AgentSession`, `AIContextProvider`. .NET 8.0, netstandard2.0, .NET Framework 4.7.2. Published 2026-05-08. |
| `Microsoft.Agents.AI.Abstractions` | 1.5.0 | GA | Base interfaces; reference this from libraries. |
| `Microsoft.Agents.AI.Workflows` | **1.5.0** | GA | `WorkflowBuilder`, `AgentWorkflowBuilder`, `RoundRobinGroupChatManager`, `Magentic` orchestration (1.5.0, marked `Experimental`). |
| `Microsoft.Agents.AI.OpenAI` | 1.5.0 | GA | OpenAI/Azure OpenAI integration helpers (`AsIChatClient`, `AsAIAgent`). |
| `Microsoft.Agents.AI.Foundry` | preview | Preview | Foundry hosted agents. |
| `Microsoft.Agents.AI.Hosting` | **1.5.0-preview.260507.1** | **Still PREVIEW** | `AddAIAgent` DI extension. Promote with care. |
| `Microsoft.Agents.AI.Workflows.Declarative` | 1.5.0-preview | Preview | YAML / declarative workflows. |
| `Microsoft.Agents.AI.Anthropic` | 1.5.0-preview | Preview | Anthropic Claude connector. |
| `Microsoft.Agents.AI.Hyperlight` | 1.5.0-alpha | Alpha | CodeAct sandbox via Hyperlight. New in 1.4. |
| `Microsoft.Extensions.AI` | **10.5.1** | GA | Pinned by MAF 1.5.0 (PR #5652 bumped it from 10.5.0). |
| `Microsoft.Extensions.AI.Abstractions` | 10.5.1 | GA | `IChatClient`, `AIFunction`, `AIFunctionFactory`, `DelegatingChatClient`, `ChatOptions`, `ChatResponseFormat`. |
| `ModelContextProtocol` | **1.0.0** | GA, released March 5, 2026 — per the Microsoft .NET Blog post by Mike Kistler ("Release v1.0 of the official MCP C# SDK", 2026-03-05): "The Model Context Protocol (MCP) C# SDK has reached its v1.0 milestone, bringing full support for the 2025-11-25 version of the MCP Specification". |
| `ModelContextProtocol.AspNetCore` | 1.0.0 | GA | HTTP/SSE host. |

> **Anti-pattern:** Do not take a dependency on `Microsoft.Agents.AI.Hosting` GA contracts; it is still preview at 1.5.0. Wrap its `AddAIAgent` calls behind your own `IAgentRegistry` interface so you can swap it without rewriting registrations.

### 2. `ChatClientAgent` is sealed — the construction pattern

From `Microsoft.Agents.AI.ChatClientAgent` (Microsoft Learn API reference):

```csharp
public sealed class ChatClientAgent : Microsoft.Agents.AI.AIAgent
```

`ChatClientAgent` is **sealed**. You cannot inherit from it. To customize behaviour you either (a) wrap the inner `IChatClient` with `DelegatingChatClient` middleware before passing it in, (b) attach an `AIContextProvider` via `ChatClientAgentOptions.AIContextProviderFactory`, or (c) build a custom `AIAgent` from `Microsoft.Agents.AI.Abstractions`. The constructor surface in 1.5.0:

```csharp
// Short form
new ChatClientAgent(IChatClient chatClient,
                    string? instructions = null,
                    string? name = null,
                    string? description = null,
                    IList<AITool>? tools = null);

// Options form (recommended for production)
new ChatClientAgent(IChatClient chatClient, ChatClientAgentOptions options);
```

`ChatClientAgentOptions` exposes:

- `Name`, `Description`, `Instructions` (string)
- `ChatOptions` — `Microsoft.Extensions.AI.ChatOptions` carrying `Tools`, `ResponseFormat`, `Temperature`, `MaxOutputTokens`, etc.
- `AIContextProviderFactory` — `Func<AIContextProviderFactoryContext, AIContextProvider>` (single provider only — there is no `AggregateAIContextProvider` on .NET as of 1.5.0; an open issue #2933 tracks parity with Python's multi-provider auto-aggregation)
- `ChatMessageStoreFactory` — `Func<ChatMessageStoreFactoryContext, ChatMessageStore>`
- `UseProvidedChatClientAsIs` (bool, default false) — when false, MAF wraps the supplied `IChatClient` with `FunctionInvokingChatClient` and friends; when true, MAF uses the client exactly as provided (essential when you have already composed the pipeline yourself).
- `RequirePerServiceCallChatHistoryPersistence` (bool) — activates `PerServiceCallChatHistoryPersistingChatClient`, which saves intermediate tool results between each service call inside a tool loop. Strongly recommended for regulated workloads to satisfy crash-recovery and audit requirements.

**`RunAsync` signature** (`Microsoft.Agents.AI.ChatClientAgent.RunAsync`):

```csharp
public override Task<AgentResponse> RunAsync(
    string message,
    AgentSession? session = null,
    ChatClientAgentRunOptions? options = null,
    CancellationToken cancellationToken = default);
```

Overloads accept `ChatMessage`, `IEnumerable<ChatMessage>`, or no message (continuation). `AgentResponse` exposes `.Text`, `.Messages`, `.AdditionalProperties`. Streaming counterpart: `RunStreamingAsync(...) → IAsyncEnumerable<AgentRunResponseUpdate>`.

> **Breaking change vs. preview (cut over by 1.0 GA, April 3, 2026):** the parameter `thread` was renamed to `session` (type `AgentSession`). `ChatClientAgentOptions.Instructions` is retained, but the short-form constructor pattern in samples uses positional `instructions:` arguments instead of options where possible.

### 3. `AIContextProvider` — per-turn context injection

Abstract base in `Microsoft.Agents.AI`:

```csharp
public abstract class AIContextProvider
{
    public virtual ValueTask<AIContext> InvokingAsync(
        AIContextProvider.InvokingContext context, CancellationToken ct = default);

    public virtual ValueTask InvokedAsync(
        AIContextProvider.InvokedContext context, CancellationToken ct = default);

    public virtual ValueTask SerializeAsync(...);
}
```

Two-phase lifecycle:

1. **`InvokingAsync`** runs immediately before the underlying `IChatClient` call. Return an `AIContext` containing additional `Instructions`, `Messages`, or `Tools` to merge into the request. Used for memory injection, RAG context, dynamic policy text, per-user PII redaction reminders, etc.
2. **`InvokedAsync`** runs after the response. Inspect request + response messages to extract state (e.g., update memory).

A single instance is attached to the agent and used across all sessions, so **never store session-specific state on the provider** — store it on the `AgentSession` state bag via `ProviderSessionState<T>` (using a unique `StateKey`).

Built-in providers include `TextSearchProvider` (RAG) and `InMemoryHistoryProvider`. .NET only supports one `AIContextProvider` per agent in 1.5.0 — compose multiple concerns manually in a single provider, or vote for issue #2933.

### 4. `AIFunctionFactory.Create` — turning C# methods into tools

From `Microsoft.Extensions.AI.AIFunctionFactory` (in 1.5.0 it ships from `Microsoft.Extensions.AI.Abstractions` 10.5.x):

```csharp
public static AIFunction Create(
    Delegate method,
    string? name = null,
    string? description = null,
    JsonSerializerOptions? serializerOptions = null);

public static AIFunction Create(
    MethodInfo method, object? target,
    string? name = null,
    string? description = null,
    JsonSerializerOptions? serializerOptions = null);

public static AIFunctionDeclaration CreateDeclaration(
    string name, string? description,
    JsonElement jsonSchema,
    JsonElement? returnJsonSchema = null);
```

No MAF-specific attributes are needed. The factory introspects parameters and pulls metadata from `[Description]` on the method and each parameter, and `[DisplayName]` for the function name (overrideable). Example:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

[Description("Resolve a counterparty by LEI against the Investec reference data.")]
static async Task<CounterpartyDto?> ResolveCounterpartyAsync(
    [Description("ISO 17442 Legal Entity Identifier (20 chars).")] string lei,
    [Description("ISO-8601 as-of date.")] DateOnly asOf,
    CancellationToken ct) => ...

AIFunction tool = AIFunctionFactory.Create(ResolveCounterpartyAsync);
```

> **Anti-pattern:** Generic parameter names (`input`, `data`) with no `[Description]`. The schema is shipped to the model verbatim — poor names = poor tool selection = wasted tokens and tool-call retries.

### 5. The `IChatClient` middleware pipeline

`Microsoft.Extensions.AI.DelegatingChatClient` is the base for chained clients. Order matters; **inner clients are closer to the model, outer clients are closer to the caller**. A regulated-bank pipeline composes (outer → inner):

```
[Audit/Budget Tracking]
   → [PII Redaction / Content Safety (Prompt Shields)]
      → [OpenTelemetry instrumentation]
         → [Tool Approval Gate (HITL)]
            → [FunctionInvokingChatClient]
               → [Distributed Cache (Redis)]
                  → [AzureOpenAIClient.AsIChatClient()]
```

Built with `ChatClientBuilder`:

```csharp
IChatClient chatClient = new AzureOpenAIClient(endpoint, new ManagedIdentityCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsBuilder()
    .UseDistributedCache(distributedCache)        // closest to model
    .UseFunctionInvocation(configure: f => {
        f.AdditionalTools = [AIFunctionFactory.Create(GetUtcNow, "get_current_time_utc",
                              "Returns current UTC time as ISO 8601.")];
        f.MaximumIterationsPerRequest = 5;
    })
    .Use(new HumanApprovalChatClient(/*queue*/))   // surfaces FunctionCallContent for approval
    .UseOpenTelemetry(sourceName: "Investec.Agents",
                      configure: o => o.EnableSensitiveData = false)
    .Use(new PiiRedactionChatClient(redactor))
    .Use(new BudgetTrackingChatClient(quotaService))
    .Build();
```

To author a middleware, inherit `DelegatingChatClient` and override **both** `GetResponseAsync` and `GetStreamingResponseAsync` (overriding only the former means streaming callers skip your logic):

```csharp
public sealed class BudgetTrackingChatClient(IChatClient inner, IQuotaService quota)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct)
    {
        await quota.CheckAsync(ct);
        var response = await base.GetResponseAsync(messages, options, ct);
        quota.Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await quota.CheckAsync(ct);
        ChatResponseUpdate? last = null;
        await foreach (var u in base.GetStreamingResponseAsync(messages, options, ct))
        { last = u; yield return u; }
        if (last is not null) quota.Record(last);
    }
}
```

For HITL approval, intercept `FunctionCallContent` in the response **before** the inner `FunctionInvokingChatClient` runs the call. Use MEAI's `FunctionApprovalRequestContent` / `FunctionApprovalResponseContent` content types to flow approvals back through chat history (the official Approvals story in MAF 1.5.0).

**Per-pipeline vs. per-call tools**: `ChatOptions.Tools` is per call (use it on `ChatClientAgentOptions.ChatOptions` to scope tools per agent). `FunctionInvokingChatClient.AdditionalTools` is per pipeline (ambient tools available to every agent that shares the pipeline). Keep ambient tools small — every tool schema is shipped on every request.

### 6. Tool scoping per agent

This is the crux for a multi-tenant agentic platform. Three layers of scoping work together:

1. **Per-pipeline ambient tools** (`FunctionInvokingChatClient.AdditionalTools`) — only for genuinely cross-cutting helpers (`get_current_time_utc`, `whoami`).
2. **Per-agent tools** — passed via `ChatClientAgentOptions.ChatOptions.Tools` at agent construction. **This is the recommended scoping unit**; each agent has its own catalog.
3. **Per-run tools** — `ChatClientAgentRunOptions` extends `AgentRunOptions` and can carry a `ChatOptions` whose `Tools` collection is merged with the agent-level options for that invocation only. Useful for conditionally exposing a privileged tool when a caller's claims permit it.

```csharp
var paymentsAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions {
    Name = "PaymentsAgent",
    Description = "Initiates payment workflows under Icarus rules.",
    Instructions = paymentsSystemPrompt,
    ChatOptions = new ChatOptions {
        Tools = [
            AIFunctionFactory.Create(GetBeneficiaries),
            AIFunctionFactory.Create(QuoteFx),
            AIFunctionFactory.Create(SubmitPayment) // requires HITL approval middleware
        ],
        ResponseFormat = ChatResponseFormat.ForJsonSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(PaymentDecision)),
            "PaymentDecision", "Outcome of a payment authorization check")
    },
    RequirePerServiceCallChatHistoryPersistence = true,
    AIContextProviderFactory = ctx =>
        new InvestecPolicyContextProvider(policyService, ctx.SerializedState)
});
```

> **Anti-pattern:** Passing one big catalog of every tool to every agent and "letting the model figure it out". Token cost, jailbreak surface, and tool-confusion all grow with catalog size. Investec/Icarus should scope tools per agent role.

### 7. MCP integration in C#

Add the official SDK alongside MAF:

```xml
<PackageReference Include="ModelContextProtocol" Version="1.0.0" />
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.0.0" />
```

MCP tools are `AITool` instances (a base of `AIFunction`) and can be registered side-by-side with native `AIFunction`s:

```csharp
await using var mcpClient = await McpClientFactory.CreateAsync(new StdioClientTransport(new()
{
    Name = "InvestecRefData",
    Command = "dotnet",
    Arguments = ["run", "--project", "RefDataMcpServer.csproj"]
}));

IList<AITool> mcpTools = (await mcpClient.ListToolsAsync()).Cast<AITool>().ToList();

var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions {
    Name = "ResearchAgent",
    Instructions = "...",
    ChatOptions = new ChatOptions {
        Tools = [.. mcpTools, AIFunctionFactory.Create(CalculateNpv)]
    }
});
```

MCP also supports remote SSE/HTTP transports — use `SseClientTransport` for cross-network MCP servers (e.g., the **ACA Dynamic Sessions MCP endpoint**: setting `isMCPServerEnabled: true` on the session pool turns it into a remote MCP server you can plug straight into MAF).

### 8. Azure Container Apps Dynamic Sessions

For the Icarus regulated-bank workload, Dynamic Sessions are the right primitive for any model-generated code, file processing on untrusted inputs, or shell automation:

- **Hyper-V isolation** per session, prewarmed pool. Per Microsoft Learn ("Use session pools in Azure Container Apps"), "containers are started using a pool of existing hardware to ensure fast startup time" — Microsoft documents this as "fast" rather than committing to a specific sub-second figure. Each session is ephemeral and destroyed after the configurable cooldown.
- **Three runtimes**: Python (`PythonLTS`, with NumPy, pandas, and scikit-learn pre-installed per Microsoft Learn's "Serverless code interpreter sessions" page: "Python code interpreter sessions include popular Python packages such as NumPy, pandas, and scikit-learn"), Node.js, Shell (full Linux), plus **Custom container** session pools where you supply your own image.
- **Endpoint**: `https://<REGION>.dynamicsessions.io/subscriptions/<SUB>/resourceGroups/<RG>/sessionPools/<POOL>/executions?api-version=2025-10-02-preview&identifier=<SESSION_ID>` (the 2024 preview versions are slated for deprecation).
- **Auth**: AAD bearer token with audience `https://dynamicsessions.io`. Per Microsoft Learn, "Valid Microsoft Entra tokens are generated by an identity belonging to the Azure ContainerApps Session Executor and Contributor roles on the session pool." From an ACA-hosted agent app, use `ManagedIdentityCredential`.
- **File scope**: uploads land in `/mnt/data`; file names must be the supported character set (`A-Z a-z 0-9 - _ . @ $ & = ; , # % ^ ( )`, no `..`). Enforce **one session ID per user conversation**, never let the end-user choose the session ID, and gate every file upload against an allow-list keyed by `(tenantId, agentId, classification)`.
- **MCP mode**: setting `isMCPServerEnabled: true` on the session pool exposes the pool as an MCP server with API-key auth, consumable directly from MAF via the `ModelContextProtocol` SDK.

Wrap the Sessions API behind a single MAF tool:

```csharp
[Description("Run Python in an isolated ACA Dynamic Session against the caller's data scope.")]
static async Task<SessionExecutionResult> RunPythonAsync(
    [Description("Inline Python code. Must use only files under /mnt/data.")] string code,
    SessionContext ctx, CancellationToken ct)
{
    var sessionId = $"icarus-{ctx.TenantId}-{ctx.ConversationId}";
    return await sessionsClient.ExecuteAsync(sessionId, code, ct);
}
```

### 9. EF Core 10 + PostgreSQL JSONB for the agent catalog

Three storage modes are available via Npgsql's EF provider; for EF 10 the recommended pattern for owned/nested data is **complex types** with `ToJson()`. For string blobs (tool manifests received from MCP servers, opaque interface contracts you want to round-trip without schema awareness), use a plain `string` property mapped to `jsonb`:

```csharp
public class AgentDefinition
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = default!;
    public int Version { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string Instructions { get; set; } = default!;
    public string Model { get; set; } = default!;

    // Strongly-typed, queryable, JSONB-mapped:
    public List<ToolBinding> Tools { get; set; } = [];
    public FileScopePolicy FileScope { get; set; } = new();
    public InterfaceContract Interface { get; set; } = new();

    // Audit
    public string CreatedBy { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

protected override void OnModelCreating(ModelBuilder mb)
{
    mb.Entity<AgentDefinition>(e =>
    {
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.Slug, x.Version }).IsUnique();
        e.OwnsMany(x => x.Tools, b => b.ToJson());
        e.OwnsOne(x => x.FileScope, b => b.ToJson());
        e.OwnsOne(x => x.Interface, b => b.ToJson());
        e.HasIndex(x => x.Tools).HasMethod("gin"); // GIN on the JSONB column
    });
}
```

For **versioning with audit trails**, treat `(Slug, Version)` as the natural key and create a sibling `AgentDefinitionHistory` table that captures the full prior JSON snapshot plus actor, timestamp, and request-correlation ID. Never delete — set `RetiredAt`. PostgreSQL `jsonb` supports `@>`, `?`, `?|`, `?&` operators and GIN indexes; EF 10 will translate LINQ traversals like `agent.Tools.Any(t => t.Name == "SubmitPayment")` to `jsonb_array_elements_text` queries.

### 10. Prompt management

`ChatClientAgent.Instructions` is a single string set at construction time. It cannot be mutated on a live agent — `ChatClientAgentOptions` is **cloned for immutability** when the agent is created. To vary the prompt:

- **Per turn**: pass `ChatClientAgentRunOptions(new ChatOptions { Instructions = "..." })` and the framework merges the run-level instructions with the agent-level ones.
- **Dynamic context**: return additional `Instructions` from `AIContextProvider.InvokingAsync` — this is the recommended layer for tenant policy, user role hints, content-safety reminders, and time-of-day operating rules.
- **System mode vs. chat mode**: instantiate two `ChatClientAgent` instances against the same `IChatClient` pipeline, one with the system-mode prompt and one with the chat-mode prompt. Both share the middleware, both are cheap to create.

Multi-layer assembly order that has held up in practice:

1. Immutable role prompt (`Instructions` at construction) — captures purpose, tone, prohibited actions.
2. Policy layer (`AIContextProvider` → `Instructions`) — per-tenant compliance rules, jurisdiction (e.g., FCA vs. PRA vs. SARB).
3. Memory layer (`AIContextProvider` → `Messages`) — extracted user facts.
4. Retrieval layer (`TextSearchProvider`) — RAG snippets.
5. Per-turn overrides via `ChatClientAgentRunOptions` — short-lived A/B variants.

> **Anti-pattern:** Stuffing rapidly-changing policy text into the immutable `Instructions`. You'll rebuild your agent catalog every policy change. Put it in a context provider that reads from a versioned `PolicyStore` table.

### 11. Structured output

`ChatResponseFormat.ForJsonSchema(JsonElement schema, string schemaName, string schemaDescription)` plus `AIJsonUtilities.CreateJsonSchema(typeof(T))` is the canonical pattern. Set it on `ChatClientAgentOptions.ChatOptions.ResponseFormat`. Deserialize with `response.Deserialize<T>(JsonSerializerOptions.Web)`. Works against Azure OpenAI, OpenAI, and any provider whose `IChatClient` reports support; some non-OpenAI providers (Qwen, DeepSeek) require schema injection via the system prompt — write a `DelegatingChatClient` to do that or pick a supporting model.

### 12. Observability — first-class

MAF emits OpenTelemetry traces, logs, and metrics aligned with the **OpenTelemetry GenAI Semantic Conventions**. Microsoft confirms the standard attribute set in the Community Hub post "Monitor AI Agents on App Service with OpenTelemetry and the New Application Insights Agents View" — for example, "Agent dropdown filter — A dropdown populated by `gen_ai.agent.name` values from your telemetry", with each user request producing an `invoke_agent` root span and nested `chat` child spans, alongside `gen_ai.system`, `gen_ai.request.model`, `gen_ai.usage.input_tokens`, and `gen_ai.usage.output_tokens` attributes. Two activity sources: the default `Experimental.Microsoft.Agents.AI` (override via `UseOpenTelemetry(sourceName: ...)`) and the underlying `Microsoft.Extensions.AI`. Application Insights' **Agents (Preview)** blade lights up automatically if both `name` on the `ChatClientAgent` and the source name are set.

Wire it on both layers — IChatClient and ChatClientAgent — only if you accept the duplication; the agent-level span wraps the chat-level span. For tool-level tracing inside the function-invocation loop you need the IChatClient layer instrumented (the agent layer cannot see the inner loop).

Set `EnableSensitiveData = false` in production for Investec — prompts and responses are explicitly excluded from telemetry under the GenAI conventions when this flag is off.

> **Anti-pattern:** Relying on the fire-and-forget OTel exporter to a single Azure Monitor endpoint for compliance-grade audit retention. Deploy a sidecar OTel Collector to buffer, retry, and persist spans independently of process lifetime, and forward to both Application Insights and a long-retention store (e.g., Azure Data Explorer).

### 13. Content Safety integration

For regulated banking, drop `Azure AI Content Safety Prompt Shields` (direct + indirect attacks, replacing the older Jailbreak Risk Detection) into the **outer** middleware ring before the model layer. Run `ShieldPrompt` synchronously for the user prompt, asynchronously for streaming outputs. The Prompt Shields API returns a binary attack classification per category. Translate a positive detection into a structured `ChatResponse` containing a refusal with a refusal reason code your agent UI can render. Pair with a custom blocklist for Investec-specific banned phrases and customer PII patterns.

### 14. Multi-agent orchestration in 1.5

`Microsoft.Agents.AI.Workflows` provides:

- `AgentWorkflowBuilder.BuildSequential(writer, editor)` — pipeline.
- `AgentWorkflowBuilder.CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = 5 }).AddParticipants(...).Build()` — group chat.
- Handoff Orchestration — refactored in 1.2.0 with HITL support.
- **Magentic Orchestration** — new in 1.5.0, marked **`Experimental`** in #5704. Do not put it on a regulated production path yet.

Graph workflows are composed from `Executor<T>` nodes with `WorkflowBuilder.AddEdge(...)`; agents become executors via `AsAgent()`. Each agent in a group chat owns its own `AgentSession`; the orchestrator broadcasts conversation state between turns.

---

## Details

### 14.1 What is — and is not — in 1.5.0 vs. 1.0 GA

| Area | 1.0 GA (Apr 3 2026) | 1.5.0 (May 8 2026) | Breaking? |
|---|---|---|---|
| `ChatClientAgent` (sealed) | ✔ | ✔, no surface change called out | No |
| `ChatClientAgentOptions` | ✔ | ✔ | No |
| `AgentSession` rename from `AgentThread` | done by 1.0 GA | unchanged | (Breaking 1.0 vs preview only) |
| `RunAsync(message, session, options, ct)` | ✔ | ✔ | No |
| `AIContextProvider` single-attach | ✔ | ✔ (multi-attach tracked in #2933) | — |
| `FunctionInvokingChatClient` | MEAI 10.4 | MEAI 10.5.1 | No |
| OpenTelemetry packages | (1.0 GA baseline; specific version not verified in release notes) | bumped to 1.15.3 per the GitHub `dotnet-1.4.0` release tag: ".NET: Bump OpenTelemetry packages to 1.15.3 by @SergeyMenshykh in #5478" | No |
| Handoff Orchestration | basic | refactored + HITL (1.2.0 #5174) | Behaviour change |
| Magentic Orchestration | absent | added Experimental (1.5.0 #5595) | New, experimental |
| WebBrowsingTool allow-listing | n/a | added (1.5.0 #5605) | New |
| Hyperlight CodeAct sandbox | n/a | new package `Microsoft.Agents.AI.Hyperlight` 1.5.0-alpha (1.4.0 #5329) | New, alpha |
| MEAI version | 10.4.0 | **10.5.1** (1.5.0 #5652) | No public API breaks reported in MEAI 10.5 |
| Foundry per-call `x-client` header | n/a | added (1.5.0 #5652) | New |
| `function_call_output.output` wire format | structured | **JSON string** on wire (1.5.0 #5705) | Wire-level — review if you persist transcripts |
| File-based skill scripts | scalar args | **`string[]` args** | **[Breaking] (1.4.0 #5475)** — only impact if you use file skills |

Only one item carries an explicit `[Breaking]` tag (`string[]` for file-based skill scripts in 1.4.0). The function-call output wire change in 1.5.0 is not flagged breaking but **will alter what you persist in audit logs** — review any code that parses `FunctionResultContent.Result`.

### 14.2 Putting it all together — the agent factory

```csharp
public sealed class AgentFactory(
    IChatClientFactory chatClientFactory,         // returns the composed pipeline above
    IPolicyStore policies,
    IFileScopeService fileScope,
    IToolRegistry toolRegistry)                   // resolves an AgentDefinition's ToolBindings to AIFunctions
{
    public AIAgent Build(AgentDefinition def, TenantContext tenant)
    {
        IChatClient chat = chatClientFactory.Get(def.Model, tenant);

        var tools = toolRegistry.Resolve(def.Tools, tenant, fileScope.For(tenant, def));

        return new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = def.Slug,
            Description = def.Description,
            Instructions = def.Instructions,
            ChatOptions = new ChatOptions
            {
                Tools = tools,
                Temperature = 0.2f,
                ResponseFormat = def.Interface.ResponseSchema is null
                    ? ChatResponseFormat.Text
                    : ChatResponseFormat.ForJsonSchema(
                          JsonElement.Parse(def.Interface.ResponseSchema),
                          def.Interface.SchemaName,
                          def.Interface.SchemaDescription)
            },
            RequirePerServiceCallChatHistoryPersistence = true,
            AIContextProviderFactory = ctx => new IcarusPolicyMemoryProvider(
                policies, tenant, def, ctx.SerializedState)
        });
    }
}
```

The `IChatClient` pipeline is **constructed once per (model, tenant)** and shared across all agents for that tenant to amortize middleware allocation. Each `ChatClientAgent` is cheap (essentially a wrapper holding options and a reference to the shared `IChatClient`).

---

## Recommendations

**Stage 1 — Foundations (weeks 1–2):**
- Pin `Microsoft.Agents.AI` 1.5.0, `Microsoft.Extensions.AI` 10.5.1, `ModelContextProtocol` 1.0.0. **Don't** depend on `Microsoft.Agents.AI.Hosting` GA contracts yet; wrap it. **Don't** put Magentic Orchestration in regulated paths until it leaves `Experimental`.
- Stand up the central `IChatClient` builder (`UseDistributedCache → UseFunctionInvocation → HITL → UseOpenTelemetry → PII Redaction → Budget`). Cover both `GetResponseAsync` and `GetStreamingResponseAsync` in every custom `DelegatingChatClient`.
- Provision two ACA Dynamic Session pools — one Python (`PythonLTS`, network egress disabled by default), one Shell — with managed-identity role assignments and policy that forces one session ID per conversation.

**Stage 2 — Agent catalog & registry (weeks 3–4):**
- Implement `AgentDefinition` in PostgreSQL with `jsonb` columns and a sibling `*_History` table. Index `Tools` with GIN. Treat `(Slug, Version)` as immutable; new policy = new version.
- Build the `AgentFactory` that resolves a definition + tenant into a `ChatClientAgent` with scoped tools.
- Author the `IcarusPolicyMemoryProvider : AIContextProvider` that injects policy + memory in `InvokingAsync` and persists extracted state via `SerializeAsync`.

**Stage 3 — Tools & MCP (weeks 5–6):**
- Convert internal capabilities to `AIFunction`s with rich `[Description]` metadata.
- Stand up at least one MCP server for shared reference data (refdata, market data) using `ModelContextProtocol.AspNetCore` — multiple agents can consume it without each owning the integration.
- Add the ACA Dynamic Sessions MCP endpoint (`isMCPServerEnabled: true`) for code interpreter tools.

**Stage 4 — Observability & Safety (weeks 7–8):**
- Wire OpenTelemetry on the IChatClient layer (sourceName `Investec.Icarus.Chat`) and the agent layer (sourceName `Investec.Icarus.Agents`); keep `EnableSensitiveData = false`. Deploy an OTel Collector sidecar.
- Add Azure AI Content Safety Prompt Shields in the outer middleware ring.
- Add a HITL `DelegatingChatClient` that surfaces `FunctionCallContent` to a per-tenant approval queue for any tool whose `ToolBinding.RequiresApproval` is true.

**Decision thresholds:**
- **Promote `Microsoft.Agents.AI.Hosting` to a production dependency** only when it ships GA (not 1.5.0-preview). Until then, keep its `AddAIAgent` usage behind an internal abstraction.
- **Adopt Magentic Orchestration** only when the `Experimental` attribute is removed in a future release.
- **Re-evaluate the JSONB-vs-complex-types choice** if EF 10's complex-type tooling proves limiting; the `string`-typed `jsonb` escape hatch always remains.
- **Move from `gpt-5.x` to a Foundry-hosted Claude or Gemini** by changing one `IChatClient` factory line — the agent code does not change. If a provider doesn't enforce structured output natively, drop in the schema-injection middleware described above.

---

## Caveats

- **API doc is preview-labelled.** Several Microsoft Learn pages still carry the preview-package banner even though the underlying `Microsoft.Agents.AI` 1.5.0 is GA. The class shapes documented above (`ChatClientAgent`, `ChatClientAgentOptions`, `AIContextProvider`, `RunAsync`) are stable; the exact set of helper extensions in samples (`.AsBuilder().UseOpenTelemetry().Build()` vs. `.WithOpenTelemetry()`) has shifted at least once — only the `AsBuilder/UseX/Build` chain is reliable.
- **`.NET only supports one AIContextProvider` per agent** in 1.5.0. The Python side auto-aggregates multiple providers; .NET does not. Compose into a single provider until issue #2933 lands.
- **`function_call_output.output` is now a JSON string on the wire** (1.5.0 #5705). Persisted audit trails from earlier preview versions may have structured content; budget for a small migration if you persist function results verbatim.
- **The release notes I relied on are auto-generated from PR titles** — the repository does not publish a hand-curated `dotnet/CHANGELOG.md`. There may be unannounced source-compatible refinements; if zero surface change is critical, run an API-baseline diff between `dotnet-1.0.0` and `dotnet-1.5.0` packages.
- **`AddAIAgent` and the rest of `Microsoft.Agents.AI.Hosting` is preview at 1.5.0-preview.260507.1.** Treat it as a convenience, not a contract.
- **Dynamic Sessions REST API versions older than `2025-10-02-preview` will be deprecated.** Pin to the latest preview API version in your sessions client; do not consume the `2024-02-02-preview` URLs anymore.
- **No native multi-provider `AIContextProvider` aggregation, no native batch tool approval UI, no native cost-budget middleware** — all three must be hand-rolled. Build them once, share across agents.
- **`UseProvidedChatClientAsIs = true` opt-out**: if you've already composed your `IChatClient` pipeline and pass the result to `ChatClientAgent`, set `UseProvidedChatClientAsIs = true` to stop MAF from wrapping it again with its default function-invoking client. Otherwise you may get *two* function-invocation loops in your trace, double tool charges, and broken approval middleware ordering.
- **OpenTelemetry baseline version in MAF 1.0 GA is unverified.** The 1.15.3 bump (PR #5478, in 1.4.0) is confirmed; the prior pinned version is not stated in any release note I could find. If your compliance posture requires a specific OTel version, inspect the `Microsoft.Agents.AI` 1.0.0 .nupkg dependencies directly.
- **The Dynamic Sessions session-pool allocation latency is documented qualitatively** ("fast startup time" via prewarmed hardware, per Microsoft Learn). No specific sub-second SLA is published; do not commit to startup-time targets without your own measurements.