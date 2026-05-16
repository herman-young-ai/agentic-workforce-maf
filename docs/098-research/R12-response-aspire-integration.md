# AI Agent Orchestration Platform on .NET — Solution Architecture Research (May 2026)

## TL;DR
- **Microsoft Agent Framework (MAF) 1.0 GA shipped 3 April 2026** (`Microsoft.Agents.AI` v1.0.0) and is the unified successor to Semantic Kernel + AutoGen. It builds on `Microsoft.Extensions.AI` 10.5.x (`IChatClient`), supports first-party connectors for Azure OpenAI, OpenAI, Anthropic Claude, Bedrock, Gemini and Ollama, ships a graph-based workflow engine, declarative YAML, three-layer middleware (agent / function / chat), built-in OpenTelemetry GenAI semantics, and ASP.NET Core hosting via `Microsoft.Agents.AI.Hosting` (`builder.AddAIAgent(...)`).
- **Aspire 13.3 (released ~April 2026)** is the current stable. It is the canonical local-dev orchestrator and now also a first-class deployer to Azure Container Apps (via `azd` and Bicep), Docker Compose, and Kubernetes/AKS (preview). `ServiceDefaults` provides OpenTelemetry, health probes, service discovery, and Polly-based HTTP resilience out of the box; integrations exist for PostgreSQL/EF Core (`Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 13.x), Redis (`Aspire.StackExchange.Redis` 13.x), Azure AI Inference (`Aspire.Azure.AI.Inference`), and JavaScript/Vite/React (`Aspire.Hosting.JavaScript` / `AddViteApp`). Container Apps deployments get per-resource managed identities and Key Vault wiring automatically.
- **For the platform**: combine MAF for agent/workflow definition, `Microsoft.Extensions.AI` for `IChatClient` plumbing (multi-provider DI + middleware/delegating handlers), Aspire 13.3 to compose a BFF API + background Worker (recommended as separate Container Apps) + React/Vite frontend + PostgreSQL+pgvector + Redis (cache, distributed cache, SignalR backplane, MAF chat-history store), and Azure AI Foundry as a single endpoint that fronts Azure OpenAI **and** Anthropic Claude (Sonnet 4.6 / Opus 4.7 / Haiku 4.5 / Mythos preview) via the `/anthropic` route. **No public documentation exists for any "Investec Avalanche" scaffold** — treat it as an internal-only tool and integrate it as a wrapper/preprocessor that emits Aspire AppHost projects + Bicep modules consumed by Container Apps.

---

## Key Findings

### Stable versions / package matrix (confirmed May 2026)
| Component | Stable version | Primary NuGet |
|---|---|---|
| Microsoft Agent Framework | 1.0.0 (GA 3 Apr 2026) | `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Hosting`, `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Foundry`, `Microsoft.Agents.AI.DurableTask`, `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` |
| Microsoft.Extensions.AI | 10.5.x stable (GA) | `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.Abstractions`, `Microsoft.Extensions.AI.OpenAI` (10.5.1) |
| Aspire | 13.3 (Aspire 13 rebrand from ".NET Aspire"; 13.1/13.2/13.3 are minor releases on the 13.x line) | `Aspire.AppHost.Sdk`, `Aspire.Hosting.AppHost`, plus integration packages |
| Aspire Postgres EF Core | 13.2.2 | `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` |
| Aspire Redis (StackExchange) | 13.2.2 | `Aspire.StackExchange.Redis`, `Aspire.StackExchange.Redis.DistributedCaching`, `Aspire.StackExchange.Redis.OutputCaching` |
| Aspire Azure AI Inference | 13.x | `Aspire.Azure.AI.Inference` |
| Aspire JavaScript / Vite | 13.x (renamed from `Aspire.Hosting.NodeJs`) | `Aspire.Hosting.JavaScript` |
| pgvector EF Core | 0.3.0 (EF Core 9/10) | `Pgvector`, `Pgvector.EntityFrameworkCore` |
| Azure OpenAI SDK (.NET) | 2.1.0 | `Azure.AI.OpenAI` |
| Azure AI Inference SDK | stable | `Azure.AI.Inference` |
| SignalR Redis backplane | 10.0.x | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` |

### Microsoft Agent Framework 1.0 — what's stable, what's still preview
**Stable (GA) at 1.0:** core `AIAgent` / `ChatClientAgent` abstraction; provider connectors (Foundry, Azure OpenAI, OpenAI, Anthropic, Bedrock, Gemini, Ollama); middleware pipeline (agent / function / chat); graph-based workflow engine; orchestration patterns (sequential, concurrent, handoff, group chat, Magentic-One) including streaming, checkpointing, human-in-the-loop, pause/resume; declarative YAML for both agents (`AgentFactory.CreateFromYaml`) and workflows; A2A and MCP integration; agent memory & context providers (in-memory, Mem0, Redis, Neo4j, custom).
**Still preview-tagged (per Microsoft) even at 1.0:** DevUI, Foundry-hosted agents, some advanced orchestration permutations, `Microsoft.Agents.AI.DurableTask` for distributed/durable workflows, `Microsoft.Agents.AI.Hosting.AzureFunctions`, A2A 1.0 protocol completeness, the Aspire `Aspire.Hosting.GenAI`/Foundry typed catalog adapters.

### Architecture pattern that emerged from the research
For an Aspire-orchestrated agent platform, the consensus pattern is **separate Container Apps** rather than a single process:

```
┌─────────────┐    ┌──────────────────┐    ┌──────────────────┐
│ React+Vite  │───▶│ BFF API (ASP.NET)│───▶│ Worker (Worker   │
│  frontend   │    │ - SignalR hub    │    │ Service / MAF    │
│ (Container) │    │ - REST endpoints │    │ DurableTask host)│
└─────────────┘    │ - Auth / BFF     │    │ - Long-running   │
                   │ - MAF AddAIAgent │    │   workflows      │
                   └──────────────────┘    └──────────────────┘
                            │  ▲                    │  ▲
                            ▼  │                    ▼  │
                    ┌─────────────────────────────────────┐
                    │ Redis (cache + SignalR backplane    │
                    │  + MAF ChatHistoryProvider          │
                    │  + Worker queue / pub-sub bus)      │
                    └─────────────────────────────────────┘
                                       │
                                       ▼
                    ┌─────────────────────────────────────┐
                    │ PostgreSQL + pgvector (audit,       │
                    │  embeddings, durable workflow state)│
                    └─────────────────────────────────────┘
                                       │
                                       ▼
                  ┌──────────────────────────────────────────┐
                  │ Azure AI Foundry resource                │
                  │  ├─ Azure OpenAI deployments (gpt-5/4o)  │
                  │  └─ Anthropic Claude deployments         │
                  │     (Sonnet 4.6, Opus 4.7, Haiku 4.5)    │
                  └──────────────────────────────────────────┘
```

Run as **two Container Apps**: a BFF (HTTP-ingressed, scale-to-N on HTTP concurrency) and a Worker (no ingress, scale on Redis queue length / KEDA). Aspire 13.3 makes this trivial — `AddProject<>` for the API, `AddProject<>` for the worker, `AddRedis`, `AddPostgres` with pgvector image, then `azd up` produces one Bicep module per resource (~6–10 modules for a typical setup).

### Investec "Avalanche" scaffold
**No public documentation exists for any Investec internal tool named "Avalanche" that scaffolds .NET Aspire + Azure Container Apps + Bicep + Azure DevOps pipelines.** Searches against multiple query variations returned only the Avalanche L1 blockchain, the Avalanche.VC fund, an unrelated "Alloy Aspire Scaffold" by Avantibit for Optimizely, and consumer fitness/finance products — nothing about Investec's tooling. Treat Avalanche as an **internal-only artifact** and design the architecture so it integrates cleanly with public, documented patterns. Recommended interop approach is documented under *Details → Avalanche integration*.

---

## Details

### 1. Microsoft Agent Framework

**Defining an agent (the canonical pattern at GA 1.0):**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Bare minimum: any IChatClient → ChatClientAgent
IChatClient chat = /* see §3 below */;
AIAgent agent = new ChatClientAgent(
    chat,
    instructions: "You are a helpful assistant.",
    name: "Assistant",
    tools: [ AIFunctionFactory.Create(MyTool.Foo) ]);

var response = await agent.RunAsync("Hello");
Console.WriteLine(response.Text);
```

**Breaking change at 1.0 to be aware of:** `Instructions` is no longer a property on `ChatClientAgentOptions`; it is now a constructor argument. The `RunAsync` thread parameter was renamed to `session` (type `AgentSession`). The framework moved from `Microsoft.Extensions.AI` 9.x preview to **stable 10.4.1+**.

**Three providers' "happy path" idioms (all return `IChatClient`):**

```csharp
// Azure OpenAI (deployment-based)
IChatClient azureOpenAI = new AzureOpenAIClient(
        new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();

// Azure AI Foundry (multi-model, including Claude)
IChatClient foundry = new AIProjectClient(
        new Uri(projectEndpoint), new DefaultAzureCredential())
    .GetProjectOpenAIClient()
    .GetProjectResponsesClient()
    .AsIChatClient(deploymentName);

// Anthropic via Foundry — uses Azure.AI.Inference / OpenAI client
//  pointed at https://<resource>.services.ai.azure.com/anthropic
//  or the Azure AI Inference ChatCompletionsClient
IChatClient claude = new ChatCompletionsClient(
        new Uri($"https://{resource}.services.ai.azure.com/anthropic"),
        new DefaultAzureCredential())
    .AsIChatClient("claude-sonnet-4-6");
```

**ASP.NET Core / DI integration (via `Microsoft.Agents.AI.Hosting`):**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var openai = builder.AddAzureOpenAIClient("openai");
openai.AddChatClient("gpt-4o-mini")
    .UseFunctionInvocation()
    .UseOpenTelemetry(c => c.EnableSensitiveData = builder.Environment.IsDevelopment());

builder.AddAIAgent("ChatAgent", (sp, key) =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    return new ChatClientAgent(
        chat,
        instructions: "You are a writing assistant.",
        name: key,
        tools: [ AIFunctionFactory.Create(MyTools.Search) ]);
});
```

`AddAIAgent` registers the agent as a **keyed singleton** of `AIAgent`, resolvable via `[FromKeyedServices("ChatAgent")] AIAgent agent`. There are overloads that take instructions directly, instructions+`IChatClient`, or a full factory delegate.

**Multi-model routing** — register multiple keyed `IChatClient` instances and multiple keyed `AIAgent` instances, then route at the application layer (e.g., a request feature that picks `claude-opus-4-7` for hard reasoning, `gpt-4o-mini` or `claude-haiku-4-5` for cheap classification). The Foundry "model router" can also do this server-side — pricing/quota documentation explicitly mentions model-router routing across `Opus 4.1 / Sonnet 4.5 / Haiku 4.5`.

**Middleware (three layers, all chainable via the builder):**

```csharp
var middlewareEnabledChatClient = chatClient
    .AsBuilder()
    .Use(getResponseFunc: CostTrackingChatMiddleware,
         getStreamingResponseFunc: CostTrackingStreamingMiddleware)
    .UseFunctionInvocation()
    .UseOpenTelemetry(loggerFactory, sourceName: "AgentPlatform")
    .Build();

// Or at agent level:
var hardenedAgent = originalAgent
    .AsBuilder()
    .Use(runFunc: SecurityGateAgentMiddleware,
         runStreamingFunc: SecurityGateAgentStreamingMiddleware)
    .Use(FunctionApprovalMiddleware) // function/tool middleware
    .Build();
```

The three middleware kinds (each composable into a chain):
- **Agent middleware** — wraps `RunAsync` / `RunStreamingAsync`. Use for cost ledgers, rate limits, tenant isolation, prompt-injection scanners, end-user audit logging.
- **Function (tool) middleware** — intercepts tool calls. Use for argument validation, sensitive-action approval gates, mocking in tests.
- **Chat (`IChatClient`) middleware** — wraps the underlying chat call. Use for caching (`UseDistributedCache`), token telemetry, redaction. Function-calling middleware only fires for agents whose chat client is wrapped with `FunctionInvokingChatClient` (which `ChatClientAgent` does by default).

**Workflows.** Two flavors at 1.0:
- **Code (graph) workflows.** `WorkflowBuilder` composes `Executor` nodes connected by edges. Built-in patterns: sequential, concurrent (fan-out / fan-in), handoff, group chat, Magentic-One. Supports streaming, checkpointing, conditional routing, `RequestPort` for human-in-the-loop pause/resume.
- **Declarative YAML.** `AgentFactory.CreateFromYaml(...)` for agents; the equivalent factory loads workflows. YAML files are version-controllable; `kind: Prompt`, `kind: Agent`, `kind: Question`, `kind: Sequential` etc. are first-class node kinds. `Microsoft.Agents.AI.DurableTask` adds a Durable-Task-Scheduler-backed runner that survives process restarts and supports distributed executor placement and built-in observability dashboards.

**OpenTelemetry.** MAF emits traces, logs and metrics following the OpenTelemetry GenAI semantic conventions. The `.UseOpenTelemetry()` builder hook is the single line that opts in. Spans you'll see in the dashboard:
- `invoke_agent <agent_name>` — top-level per agent run
- `chat <model_name>` — underlying model call (records token counts; full prompts only when `EnableSensitiveData = true`)
- `execute_tool <function_name>` — per tool call

Two activity sources to subscribe to from a custom tracer provider: `*Microsoft.Extensions.AI` and `*Microsoft.Extensions.Agents*`. Aspire 13.2's "GenAI visualizer" auto-detects these and renders chat exchanges with markdown/multimodal preview.

**Session management & persistence.**
- `AgentSession` (renamed from "thread" at GA) carries chat history + state-bag.
- Two storage modes: **service-managed** (Foundry persistent agents, OpenAI Responses API → only a `conversationId` is stored client-side) and **client-managed** (`ChatHistoryProvider`).
- Default client-managed provider is `InMemoryChatHistoryProvider` with an optional `MessageCountingChatReducer` to cap history size.
- For production, implement a custom `ChatHistoryProvider` (e.g., Redis-backed) — the .NET community has converged on storing per-session message lists under a session-scoped Redis key, with the session-scoped state stored via `ProviderSessionState<T>` so that a single `ChatHistoryProvider` instance can serve all sessions safely.
- `agent.SerializeSession(session)` / `agent.DeserializeSessionAsync(json)` round-trip the entire session to/from JSON for cross-process resume.
- A Python-side `RedisChatMessageStore` ships in-box; .NET teams typically use a Redis-backed `ChatHistoryProvider` plus the standard `Aspire.StackExchange.Redis` `IConnectionMultiplexer`.

**Repos and docs.**
- GitHub: `https://github.com/microsoft/agent-framework`
- Docs: `https://learn.microsoft.com/agent-framework/`
- Blog (release notes, durable workflows, chat-history patterns): `https://devblogs.microsoft.com/agent-framework/`

---

### 2. Aspire 13.3

**Stable line.** Aspire 13.0 dropped the ".NET" prefix and added first-class Python and JavaScript support. 13.1 (Jan 2026) added MCP/agent tooling, dashboard refinements, renamed `AddAzureRedisEnterprise` → `AddAzureManagedRedis`, and cleaned up the implicit ACR provisioning. 13.2 stabilized Docker Compose publishing, added Dev Tunnels and OpenAI/Foundry typed catalogs, and shipped the GenAI visualizer. **13.3 (April 2026)** added first-class AKS/Helm deployment (preview), `aspire destroy`, browser telemetry capture, JavaScript publishing variants, and broader TypeScript/Python/Java AppHost authoring parity.

**AppHost wiring (typical platform):**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infra
var pg = builder.AddPostgres("pg")
    .WithImage("ankane/pgvector")          // pgvector-enabled image
    .WithDataVolume()
    .WithPgAdmin();
var appdb = pg.AddDatabase("appdb");
var vectordb = pg.AddDatabase("vectordb");

var redis = builder.AddRedis("cache")
    .WithDataVolume();

// Foundry / model endpoint (typed catalog in 13.2+)
var foundry = builder.AddConnectionString("ai-foundry");

// BFF API (HTTP ingress)
var api = builder.AddProject<Projects.Platform_Api>("api")
    .WithReference(appdb).WaitFor(appdb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(foundry)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Worker (background, no ingress)
var worker = builder.AddProject<Projects.Platform_Worker>("worker")
    .WithReference(appdb).WaitFor(appdb)
    .WithReference(vectordb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(foundry);

// React frontend (Vite)
builder.AddViteApp("frontend", "../frontend")
    .WithNpmPackageInstallation()
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

**ServiceDefaults (the canonical `AddServiceDefaults` from the template):**

```csharp
public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    builder.ConfigureOpenTelemetry();      // ASP.NET Core + HttpClient + runtime metrics, OTLP exporter
    builder.AddDefaultHealthChecks();      // /health (all checks) and /alive (liveness-only)
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler(); // Polly: retries, timeouts, circuit breaker, hedging, rate limiter
        http.AddServiceDiscovery();
    });
    return builder;
}
```

This gives you, free of charge: OpenTelemetry traces/metrics/logs (auto-exported when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, which Aspire injects), `/health` and `/alive` endpoints, Polly-based standard resilience (retry/timeout/circuit breaker), Microsoft service discovery (`https+http://api`).

**Health checks → Container Apps probes.** Aspire 9.5+ added startup/readiness/liveness probe primitives (`WithHttpHealthCheck`, `WaitForStart`), and Aspire 13.x maps these to the matching Container Apps probe Bicep at publish time. Tag your checks correctly:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connStr, tags: ["ready"])
    .AddRedis(redisConn, tags: ["ready"])
    .AddCheck<LlmEndpointHealthCheck>("llm", tags: ["ready"]);

app.MapHealthChecks("/alive", new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
```

A custom LLM health check is essentially a small probe that issues a 1-token chat call against a cheap model (or calls the Foundry models-list endpoint) and caches the result for ~30 seconds — keep it light to satisfy the Container Apps probe-budget rules.

**Container Apps Bicep — what `azd up` generates from a typical Aspire AppHost.**
For the topology above, `azd infra synth` produces roughly the following modules in `/infra`:
1. `main.bicep` — orchestrator
2. `resources.bicep` — RG-scoped composition
3. ACA `managedEnvironment` (shared) + Log Analytics workspace
4. Azure Container Registry (split out as its own module since 13.1)
5. `api.bicep` — Container App
6. `worker.bicep` — Container App
7. `frontend.bicep` — Container App
8. `pg.bicep` — Postgres (Flexible or container)
9. `redis.bicep` — Azure Managed Redis or container
10. `keyvault.bicep` — Key Vault for secret outputs
11. Per-app user-assigned managed identities + role assignments

Each Container App gets its **own user-assigned managed identity** by default (since Aspire 9.2). Aspire emits Bicep that:
- Stores secret outputs (Postgres password, Redis access key) in Key Vault using the `keyVaultName` known parameter.
- References them from each Container App via `secrets[].keyVaultUrl` + `identity:` (the per-app UAMI).
- Grants `AcrPull` to each app's identity for the ACR.
- Grants `Key Vault Secrets User` (`4633458b-17de-408a-b874-0445c86b69e6`) to each app's identity for the Key Vault.
- Wires Container App probes from your `WithHealthCheck` configuration.

Container Apps probes (Bicep) for each app look like:

```bicep
probes: [
  { type: 'Liveness'  httpGet: { path: '/alive', port: 8080 }
    initialDelaySeconds: 5  periodSeconds: 10 failureThreshold: 3 }
  { type: 'Readiness' httpGet: { path: '/health/ready', port: 8080 }
    initialDelaySeconds: 3  periodSeconds: 5  failureThreshold: 3 }
  { type: 'Startup'   httpGet: { path: '/health/ready', port: 8080 }
    initialDelaySeconds: 0  periodSeconds: 5  failureThreshold: 30 }
]
```

The 13.x deployment model also exposes `aspire publish`/`aspire deploy`/`aspire destroy` for the same Bicep emission outside `azd`, and `aspire deploy --environment Production` for environment-scoped runs. Either tool is supported; `azd` remains the easiest path for Azure.

---

### 3. Azure AI Foundry — Claude + Azure OpenAI from .NET

**Models in Foundry as of May 2026 (public preview / GA mix):**
- Anthropic: `claude-mythos-preview` (gated research preview), `claude-opus-4-7`, `claude-opus-4-6`, `claude-opus-4-5`, `claude-opus-4-1`, `claude-sonnet-4-6`, `claude-sonnet-4-5`, `claude-haiku-4-5`. Opus 4.6/4.7 and Sonnet 4.6 have a **1M-token context window**; older models 200K.
- Azure OpenAI: GPT-5 family, GPT-4o, GPT-4o-mini, plus reasoning and embedding deployments.

**Connecting from C#.**

```csharp
// 1) Azure OpenAI deployment via Foundry resource — full IChatClient
IChatClient gpt = new AzureOpenAIClient(
        new Uri("https://<resource>.openai.azure.com"),
        new DefaultAzureCredential())
    .GetChatClient("gpt-4o-mini")
    .AsIChatClient();

// 2) Claude on Foundry — use the /anthropic route via Azure.AI.Inference
//    This works because Foundry exposes Claude as a Direct Model with
//    Anthropic Messages API at https://<resource>.services.ai.azure.com/anthropic
IChatClient claude = new ChatCompletionsClient(
        new Uri("https://<resource>.services.ai.azure.com/anthropic"),
        new DefaultAzureCredential())  // Entra ID; or AzureKeyCredential
    .AsIChatClient("claude-sonnet-4-6"); // deployment name

// 3) Foundry Persistent Agents (server-side) → IChatClient
PersistentAgentsClient pac = new(projectEndpoint, new DefaultAzureCredential());
PersistentAgent fa = await pac.Administration.CreateAgentAsync(
    model: "gpt-4o-mini", name: "MyAgent", instructions: "...");
PersistentAgentThread thread = await pac.Threads.CreateThreadAsync();
IChatClient hostedChat = pac.AsIChatClient(fa.Id, thread.Id);
```

**Aspire 13.x integration.** `Aspire.Azure.AI.Inference` adds `builder.AddAzureChatCompletionsClient("ai-foundry").AddChatClient("deployment-name")` (and `AddKeyedChatClient` for multiple). Telemetry (`Experimental.Microsoft.Extensions.AI` activity source) flows through to the dashboard automatically when you use `IChatClient`; raw `ChatCompletionsClient` calls do not.

**Auth.** Use Entra ID (the Foundry "Azure AI User" role) in production via `ManagedIdentityCredential` rather than `DefaultAzureCredential` — the latter probes a chain that adds latency and creates audit-log noise. Roles: `Azure AI User` and `Cognitive Services User` are the supported defaults; the minimum required data action is `Microsoft.CognitiveServices/accounts/providers/*`.

**Important note on Claude rate-limit headers:** Foundry strips Anthropic's `anthropic-ratelimit-*` response headers — manage rate-limiting through Azure Monitor + your own deployment TPM cap (set per-deployment in the Foundry portal). Token-count metrics flow into Azure Monitor for the underlying Azure OpenAI and Foundry resources.

---

### 4. Microsoft.Extensions.AI

`Microsoft.Extensions.AI` 10.x is the **stable foundation** (GA on the same cadence as the core .NET extensions). Two packages matter:
- `Microsoft.Extensions.AI.Abstractions` — defines `IChatClient`, `IEmbeddingGenerator<TInput,TEmbedding>`, message/content types. This is what providers implement.
- `Microsoft.Extensions.AI` — implementation helpers, the `ChatClientBuilder` middleware pipeline (delegating-handler-style), `OpenTelemetry`/`Caching`/`FunctionInvocation` features.

Provider packages (e.g., `Microsoft.Extensions.AI.OpenAI` 10.5.1) add `.AsIChatClient()` extension methods on the underlying SDK clients.

**The `IChatClient` middleware/delegating-handler pattern.** Conceptually identical to `HttpClient` `DelegatingHandler`:

```csharp
IChatClient pipeline = new ChatClientBuilder(rawClient)
    .UseOpenTelemetry(loggerFactory, "AgentPlatform",
        c => c.EnableSensitiveData = false)
    .UseDistributedCache(cache)               // skips tool-calling responses
    .UseFunctionInvocation()                  // FunctionInvokingChatClient
    .UseLogging(loggerFactory)
    .Use((messages, options, next, ct) =>     // custom middleware (cost tracker)
    {
        var sw = Stopwatch.StartNew();
        var resp = await next(messages, options, ct);
        Costs.Record(resp.Usage, sw.Elapsed);
        return resp;
    })
    .Build();
```

**Registering multiple providers in DI** — keyed services are the recommended pattern:

```csharp
// Two providers, two keys
builder.Services.AddKeyedSingleton<IChatClient>("gpt", (sp, _) =>
    new AzureOpenAIClient(new Uri(aoEndpoint), new DefaultAzureCredential())
        .GetChatClient("gpt-4o-mini").AsIChatClient()
        .AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build());

builder.Services.AddKeyedSingleton<IChatClient>("claude", (sp, _) =>
    new ChatCompletionsClient(new Uri(claudeUri), new DefaultAzureCredential())
        .AsIChatClient("claude-sonnet-4-6")
        .AsBuilder().UseOpenTelemetry().Build());

// Then in code:
public class Router(
    [FromKeyedServices("gpt")]    IChatClient gpt,
    [FromKeyedServices("claude")] IChatClient claude) { ... }
```

**OpenTelemetry.** `UseOpenTelemetry` emits the OpenTelemetry GenAI semantic conventions (the same shape MAF emits). Aspire's GenAI visualizer (13.2+) automatically detects this source and renders prompts and responses inline in the dashboard.

---

### 5. "Investec Avalanche" scaffold — public-research conclusion and integration design

**Public availability:** None. The name does not appear in any Investec engineering blog, GitHub org, conference talk, or NuGet/Azure DevOps marketplace listing reachable as of May 2026. The closest public artifact is **Avantibit's "Alloy Aspire Scaffold"** (Optimizely-focused, unrelated), and the standard Microsoft `dotnet-scaffold-aspire` tool. Treat Avalanche as proprietary.

**How a scaffold tool of this kind typically interacts with Aspire-emitted Bicep:**
1. Avalanche presumably generates a *seed* Aspire AppHost project, the BFF and Worker projects, a `ServiceDefaults` library, an Azure DevOps multi-stage pipeline (`azure-pipelines.yml`), and a set of Bicep modules for organisational guard-rails (network, private endpoints, customer-managed keys, naming conventions, RBAC tags).
2. At deploy time Aspire's `aspire publish` (or `azd infra synth`) generates **app-level** Bicep (the Container App resources, identities, role assignments) into `/infra`. Avalanche's pre-baked **platform-level** Bicep (vnet, NSGs, private DNS zones, Key Vault, Log Analytics, Application Insights) sits in `/infra/platform` (or similar) and is referenced by Avalanche's own pipeline stage that runs *before* `azd provision`.
3. The two Bicep sets meet at well-known parameter outputs: the platform stack outputs the `managedEnvironmentId`, ACR resource id, Key Vault name, Log Analytics workspace id; Aspire-emitted Bicep accepts these via the documented Aspire **well-known parameters** (`Aspire.Hosting.Azure.AzureBicepResource.KnownParameters`): `KeyVaultName`, `Location`, `LogAnalyticsWorkspaceId`, `PrincipalId`, `PrincipalName`. Pass them through the `azure.yaml` `infra.parameters` block so `azd` resolves them automatically.
4. For Azure DevOps, the pipeline stages are typically: `validate-bicep` → `provision-platform` → `provision-app` (`azd provision --no-prompt`) → `build-and-push-images` → `deploy-app` (`azd deploy`) → `smoke-tests`. The Aspire CLI's `aspire deploy --environment <env>` is now (13.x) the single-shot equivalent for non-azd pipelines.

**Recommendation:** keep your AppHost the source of truth for the application graph; keep Avalanche the source of truth for org-level networking/security baselines. Use Aspire's `RunAsExisting` / `PublishAsExisting` APIs to bind to existing ACR, Key Vault, and Container Apps environment resources that Avalanche already provisioned — that avoids duplicate-resource errors and lets the two layers compose cleanly.

---

### 6. Architecture & deployment guidance

**BFF + Worker — same process or separate?**
- **Separate Container Apps** is the strongly recommended pattern. Reasons: independent scaling rules (HTTP concurrency for BFF, KEDA Redis-list / Service Bus length for Worker), independent ingress (BFF external, Worker internal-only), independent revisions for safer rollouts, separate identities for least-privilege RBAC, and avoidance of long-running agent runs blocking HTTP request threads. Aspire's `AddProject<Projects.Worker>` produces a standalone Container App at publish.
- Same-process is acceptable only for very small teams or as a stepping stone; you lose all of the above and your Container App will fail liveness probes during long workflow runs unless you carefully separate the in-process scheduler.

**Clean Architecture vs Vertical Slice for the agent platform:**
- For an agent orchestration platform whose commands are mostly *agent-shaped* (run agent, approve tool, resume workflow, search memory), **Vertical Slice** is the better fit. Each "slice" owns its handler + DTOs + persistence, and you can colocate the MAF middleware that's specific to that slice (e.g., the cost-tracking middleware lives with the "InvokeAgent" slice). This avoids the over-abstracted layer cake that hurts iteration speed when the LLM/prompt logic changes weekly.
- Use Clean Architecture only if you have a substantial **non-agent** domain core (financial calculations, regulatory rules) that genuinely benefits from a domain-driven inner ring.
- A pragmatic hybrid: Vertical Slices in the BFF and Worker for command/query handlers; a small shared `Platform.Domain` library for cross-cutting domain primitives (tenancy, cost ledger, audit event, embedding row).

**EF Core + PostgreSQL + pgvector.** Standard pattern (production-grade):

```csharp
// In AppHost
var pg = builder.AddPostgres("pg")
    .WithImage("ankane/pgvector")        // or pgvector/pgvector:pg16
    .WithDataVolume();
var vectordb = pg.AddDatabase("vectordb");

// In consumer project
dotnet add package Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Pgvector.EntityFrameworkCore  // 0.3.0, EF Core 9/10

// Program.cs
builder.AddNpgsqlDbContext<VectorDbContext>("vectordb",
    configureDataSourceBuilder: dsb => dsb.UseVector());

// Entity
public class Embedding {
  public Guid Id { get; set; }
  [Column(TypeName = "vector(1536)")]
  public Vector? Vector { get; set; }
  public string? SourceId { get; set; }
  public string? Chunk { get; set; }
}

// Model config
modelBuilder.HasPostgresExtension("vector");
modelBuilder.Entity<Embedding>()
  .HasIndex(e => e.Vector)
  .HasMethod("hnsw")
  .HasOperators("vector_cosine_ops")
  .HasStorageParameter("m", 16)
  .HasStorageParameter("ef_construction", 64);

// LINQ similarity query
var hits = await db.Embeddings
  .OrderBy(e => e.Vector!.CosineDistance(queryVector))
  .Take(8).ToListAsync();
```

Known gotcha: when you redeploy with Aspire to ACA using `WithDataVolume()` on a containerised Postgres, volume reuse can be lost across `azd deploy` runs (issue #9631). For production prefer **Azure Database for PostgreSQL Flexible Server** (with `pgvector` enabled in Server Parameters) instead of the container.

**SignalR + Aspire + Container Apps.** Use the StackExchange.Redis backplane:

```csharp
// AppHost
var redis = builder.AddRedis("cache");
var api   = builder.AddProject<Projects.Api>("api").WithReference(redis);

// API Program.cs
builder.AddRedisClient("cache");                    // IConnectionMultiplexer
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration.GetConnectionString("cache")!,
        opts => opts.Configuration.ChannelPrefix =
            RedisChannel.Literal("agent-platform"));
```

Important pitfall (well-documented): `AddStackExchangeRedis("cache")` accepts a **connection string**, not a connection name. With Aspire you must pull the resolved connection string from configuration (`GetConnectionString("cache")`) — passing the resource name directly produces a runtime connection failure. Set Container Apps ingress to `sticky-sessions: enabled` for the SignalR app so WebSocket upgrades stay on the same replica.

**Redis as message broker between BFF and Worker.** Two reasonable patterns:
- **Redis Streams** (`XADD`/`XREAD GROUP`) for at-least-once delivery + consumer groups — well supported by `StackExchange.Redis`. Use this for short-lived agent jobs where loss of the stream on Redis failover is tolerable.
- **Redis Pub/Sub** + a Postgres-backed work queue table for durability — produce to Postgres, signal via Pub/Sub, consume with `SELECT ... FOR UPDATE SKIP LOCKED`. This is the more durable pattern and works well alongside MAF's `DurableTask` workflow runner.

If you need stronger guarantees (dead-lettering, scheduled jobs, transactions across queue+DB), consider Azure Service Bus instead — Aspire has an integration and Container Apps' KEDA scaler supports Service Bus queue length out of the box.

**Managed Identity wiring (Bicep emitted by Aspire).** Each Container App gets a UAMI:

```bicep
identity: {
  type: 'UserAssigned'
  userAssignedIdentities: { '${apiIdentity.id}': {} }
}
configuration: {
  registries: [{ server: acr.properties.loginServer, identity: apiIdentity.id }]
  secrets: [{
    name: 'pg-password'
    keyVaultUrl: '${kv.properties.vaultUri}secrets/pg-password'
    identity: apiIdentity.id
  }]
}
```

Role assignments emitted automatically: `AcrPull` on the ACR, `Key Vault Secrets User` on the KV, plus any data-plane role configured on Aspire integrations (`Cognitive Services User` for Foundry, etc.).

**Container Apps health probes — recommended values for an agent platform:**

| Probe | Path | initialDelay | period | failureThreshold | Notes |
|---|---|---|---|---|---|
| Startup | `/health/ready` | 0s | 5s | 30 | 150s budget for cold start (model warmup, EF migrations) |
| Liveness | `/alive` | 5s | 10s | 3 | Keep cheap — process-only check |
| Readiness | `/health/ready` | 3s | 5s | 3 | Includes DB + Redis + LLM probes |

The default Aspire-generated probes (when no custom set is supplied and ingress is enabled) match these patterns, except they don't add a startup probe by default — add one explicitly via `WithHttpHealthCheck` for projects that take >30 s to warm.

---

## Caveats

- **MAF version drift.** Some MAF sub-packages remain in preview at the 1.0 GA mark — explicitly: `Microsoft.Agents.AI.DurableTask`, `Microsoft.Agents.AI.Hosting.AzureFunctions`, `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`, the AG-UI hosting bridge, Foundry-hosted agent helpers, and the DevUI. Pin exact versions in `Directory.Packages.props`; do not float to `*` on previews. The blog phrasing is "stable APIs and a commitment to long-term support" for the 1.0 surface, but Microsoft has reserved the right to evolve `Microsoft.Agents.AI.Workflows` orchestration internals.
- **Aspire pace of change.** Aspire ships major versions roughly yearly with frequent minor releases and breaking changes (13.0→13.3 included multiple). Microsoft's policy is **only the latest release is supported** — there is no LTS on Aspire. Plan for a minor-version upgrade cadence at least every 6 months.
- **Microsoft.Extensions.AI provider packages.** While `Microsoft.Extensions.AI` and `.Abstractions` are GA at 10.x, **some provider sub-packages (notably `Microsoft.Extensions.AI.OpenAI`)** have shipped recent versions tagged `preview`. The 10.5.1 line referenced widely in May 2026 third-party blogs *is* stable; prior 9.3-preview releases are not. Verify on nuget.org when pinning.
- **Investec Avalanche.** Treat anything specific to Avalanche in a deliverable as **assumption** unless the user supplies internal documentation. Do not assume a particular Bicep module structure, parameter set, or pipeline-template name. The recommended interop pattern (Aspire AppHost owns app-graph; Avalanche owns platform Bicep; bind via `RunAsExisting` / known-parameter outputs) is the Microsoft-documented "Aspire and projects with existing IaC (Bicep)" pattern, which is the safest default.
- **Claude on Foundry — preview.** Anthropic's Claude in Microsoft Foundry was in **public preview** at the time of research; Anthropic's own docs explicitly state the platform integration "is in preview" and that responses do not include Anthropic's standard `anthropic-ratelimit-*` headers. Production designs should account for Foundry-managed throttling rather than Anthropic-native rate-limit telemetry. Mythos is **gated** and prioritised for defensive-cybersecurity use; do not assume access.
- **Aspire ACA data persistence.** `AddPostgres(...).WithDataVolume()` does not reliably preserve data across `azd deploy` cycles into ACA (active issue). For any environment where you rely on persistence, use Azure Database for PostgreSQL Flexible Server with pgvector enabled — and run EF migrations explicitly from a one-shot Container App Job rather than at app startup. Aspire 13.3 added first-class Container App Jobs publishing for exactly this case.
- **SignalR gotcha.** `AddStackExchangeRedis("cache")` takes a connection-string, not a resource name. Always pull from `Configuration.GetConnectionString("cache")`. Otherwise the backplane silently fails to attach.
- **Speculation.** Forward-looking dates from Microsoft blogs ("A2A 1.0 support coming soon", "Aspire AKS Kubernetes deployment in preview", "aspire update is in preview and may change") are flagged here as *not yet GA* — verify before depending on them in production. Where I have referenced specific Claude model versions (Opus 4.7, Sonnet 4.6, Haiku 4.5, Mythos preview) those names appear in current Microsoft Learn docs and Anthropic's platform docs as of May 2026 but availability may vary by Azure region and subscription type.