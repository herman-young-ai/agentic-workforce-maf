# ADR-012: Aspire + MAF + Avalanche Integration

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R12-response-aspire-integration.md](../098-research/R12-response-aspire-integration.md)

---

## Context

Agentic Workforce Platform is built in C# using MAF, scaffolded via Investec's Avalanche tool (which generates .NET Aspire + Azure Container Apps + Bicep + Azure DevOps pipelines). We need to understand how all these pieces compose into a single deployable solution — project structure, Aspire wiring, deployment topology, health checks, telemetry.

## Decision

### 1. Solution Structure: Vertical Slice Hybrid

```
AgenticWorkforce/
├── .azuredevops/                          # FROM AVALANCHE
│   ├── pipelines/
│   │   ├── azure-deploy.yml
│   │   ├── azure-cleanup.yml
│   │   └── azure-pr-validation.yml
│   └── templates/
│       ├── stages/
│       └── variables/
│
├── infra/                                 # AVALANCHE platform Bicep + Aspire app Bicep
│   ├── platform/                          # Avalanche: VNet, NSGs, DNS, KV, Log Analytics
│   └── app/                               # Aspire-generated: Container Apps, identities, roles
│
├── src/
│   ├── AgenticWorkforce.sln
│   │
│   ├── AgenticWorkforce.AppHost/            # ASPIRE ORCHESTRATOR
│   │   └── Program.cs
│   │
│   ├── AgenticWorkforce.ServiceDefaults/    # SHARED CONFIG (health, OTel, resilience)
│   │   └── Extensions.cs
│   │
│   ├── AgenticWorkforce.Api/               # BFF API (HTTP ingress, SignalR, auth)
│   │   ├── Endpoints/                     # Vertical slices: Projects, Sessions, Executions, etc.
│   │   ├── Hubs/                          # SignalR: ProjectHub
│   │   ├── Auth/                          # Entra ID + API Key handlers
│   │   └── Program.cs
│   │
│   ├── AgenticWorkforce.Worker/            # BACKGROUND WORKER (no ingress)
│   │   ├── Workflows/                     # Durable Task orchestrators
│   │   ├── Activities/                    # Agent execution activities
│   │   └── Program.cs
│   │
│   ├── AgenticWorkforce.Agents/            # MAF AGENT DEFINITIONS (shared library)
│   │   ├── Catalog/                       # AgentFactory, AgentRegistry
│   │   ├── Tools/                         # AIFunction tools (file, search, git, web)
│   │   ├── Middleware/                    # Cost tracking, security, audit
│   │   ├── Context/                       # ProjectContextProvider, ContextAssembler
│   │   └── Prompts/                       # System prompt markdown files
│   │
│   ├── AgenticWorkforce.Domain/            # DOMAIN PRIMITIVES (shared library)
│   │   ├── Entities/                      # Project, Session, Execution, etc.
│   │   ├── Enums/
│   │   └── Interfaces/                    # Repository contracts
│   │
│   ├── AgenticWorkforce.Infrastructure/    # DATA ACCESS (shared library)
│   │   ├── Data/
│   │   │   ├── AgenticWorkforceDbContext.cs
│   │   │   ├── Repositories/
│   │   │   └── Migrations/
│   │   ├── Redis/
│   │   └── External/                      # Web search providers, content extractors
│   │
│   └── tests/
│       ├── AgenticWorkforce.Tests.Unit/
│       ├── AgenticWorkforce.Tests.Integration/
│       └── AgenticWorkforce.Tests.Architecture/
│
├── frontend/                              # REACT SPA (Vite)
│   ├── src/
│   └── package.json
│
├── azure.yaml                             # azd configuration
└── README.md
```

**Vertical Slice** for the API and Worker (each command/query owns its handler + DTOs + persistence). Small shared `Domain` library for cross-cutting primitives. `Agents` is a shared library referenced by both Api and Worker.

### 2. Aspire AppHost Wiring

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var pg = builder.AddPostgres("pg")
    .WithImage("ankane/pgvector")
    .WithDataVolume();
var appdb = pg.AddDatabase("appdb");

var redis = builder.AddRedis("cache")
    .WithDataVolume();

var foundry = builder.AddConnectionString("ai-foundry");

// BFF API (HTTP ingress, SignalR)
var api = builder.AddProject<Projects.AgenticWorkforce_Api>("api")
    .WithReference(appdb).WaitFor(appdb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(foundry)
    .WithHttpHealthCheck("/health/ready")
    .WithExternalHttpEndpoints();

// Worker (background, no ingress — agent execution + workflows)
var worker = builder.AddProject<Projects.AgenticWorkforce_Worker>("worker")
    .WithReference(appdb).WaitFor(appdb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(foundry);

// React frontend (Vite)
builder.AddViteApp("frontend", "../frontend")
    .WithNpmPackageInstallation()
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### 3. BFF + Worker Separation

**Separate Container Apps** (strongly recommended):

| Concern | BFF (Api) | Worker |
|---------|-----------|--------|
| Ingress | External HTTP | None (internal only) |
| Scaling | HTTP concurrency | KEDA (Redis queue length) |
| Responsibilities | REST API, SignalR hub, auth, short ops | Long-running workflows, agent execution, DurableTask host |
| Identity | Own UAMI (API-facing RBAC) | Own UAMI (AI Foundry, DB, sandbox RBAC) |
| Communication | Enqueues work to Redis/Postgres | Picks up work, publishes events via Redis pub/sub → SignalR |

Communication pattern:
```
User → BFF API → Redis Stream (XADD) → Worker picks up (XREADGROUP)
                                         → Runs MAF workflow
                                         → Publishes events (Redis pub/sub)
                                         → BFF SignalR hub fans out to clients
```

### 4. ServiceDefaults + MAF Extensions

```csharp
public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    // Aspire defaults: OTel, health, service discovery, Polly resilience
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });
    return builder;
}

// MAF-specific additions (call from both Api and Worker)
public static IServiceCollection AddAgenticWorkforceAgents(this IServiceCollection services,
    IConfiguration config)
{
    // Multi-provider IChatClient registration
    services.AddKeyedSingleton<IChatClient>("claude", (sp, _) =>
        new ChatCompletionsClient(
                new Uri(config["Foundry:AnthropicEndpoint"]!),
                sp.GetRequiredService<TokenCredential>())
            .AsIChatClient(config["Foundry:ClaudeDeployment"]!)
            .AsBuilder()
            .Use((inner, sp) => new BudgetEnforcingChatClient(inner, sp.GetRequiredService<IBudgetService>()))
            .Use((inner, sp) => new AuditingChatClient(inner, sp.GetRequiredService<ChannelWriter<AuditRecord>>()))
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: "AgenticWorkforce.Agents")
            .Build(sp));

    services.AddKeyedSingleton<IChatClient>("gpt", (sp, _) =>
        new AzureOpenAIClient(
                new Uri(config["Foundry:OpenAIEndpoint"]!),
                sp.GetRequiredService<TokenCredential>())
            .GetChatClient(config["Foundry:GptDeployment"]!)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry()
            .Build(sp));

    // Agent factory
    services.AddSingleton<IAgentFactory, AgentFactory>();

    // Budget service
    services.AddSingleton<IBudgetService, BudgetService>();

    return services;
}
```

### 5. Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, tags: ["ready"])
    .AddRedis(redisConnection, tags: ["ready"])
    .AddCheck<LlmEndpointHealthCheck>("llm", tags: ["ready"]);

app.MapHealthChecks("/alive", new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
```

LLM health check: lightweight probe that calls model list endpoint or issues a 1-token completion, cached 30s.

Container Apps probes (Bicep):

| Probe | Path | InitialDelay | Period | FailureThreshold |
|-------|------|-------------|--------|-----------------|
| Startup | `/health/ready` | 0s | 5s | 30 (150s budget) |
| Liveness | `/alive` | 5s | 10s | 3 |
| Readiness | `/health/ready` | 3s | 5s | 3 |

### 6. Production Deployment Topology

```
┌─── Azure Container Apps Environment (SA North) ──────────────────┐
│                                                                    │
│  container-app: agentic-workforce-api      (external HTTP, sticky)  │
│  container-app: agentic-workforce-worker   (internal only, KEDA)    │
│  container-app: agentic-workforce-frontend (external HTTP, static)  │
│                                                                    │
│  session-pool: project-sandbox           (Dynamic Sessions, Hyper-V) │
└────────────────────────────────────────────────────────────────────┘

┌─── Data ──────────────────────────────────────────────────────────┐
│  PostgreSQL Flexible Server (pgvector, zone-redundant HA)         │
│  Azure Cache for Redis (events, sessions, SignalR backplane)      │
└───────────────────────────────────────────────────────────────────┘

┌─── AI ────────────────────────────────────────────────────────────┐
│  Azure AI Foundry Project                                         │
│  ├── Claude deployments (Sonnet 4.6, Haiku 4.5, Opus 4.6)       │
│  └── Azure OpenAI deployments (GPT-4o, text-embedding-3-small)   │
└───────────────────────────────────────────────────────────────────┘

┌─── Durability ────────────────────────────────────────────────────┐
│  Azure Durable Task Scheduler (managed, Consumption SKU)          │
└───────────────────────────────────────────────────────────────────┘

┌─── Security ──────────────────────────────────────────────────────┐
│  Key Vault (secrets via UAMI, no stored keys)                     │
│  Entra ID App Registration (4 app roles)                          │
│  User-Assigned Managed Identity per Container App                 │
└───────────────────────────────────────────────────────────────────┘

┌─── Observability ─────────────────────────────────────────────────┐
│  Application Insights (OTel sink)                                 │
│  Log Analytics Workspace (audit, pgAudit, session logs)           │
└───────────────────────────────────────────────────────────────────┘

┌─── Audit ─────────────────────────────────────────────────────────┐
│  Azure Blob Storage (WORM, version-level, ZRS)                    │
│  Event Hubs Standard (1 TU)                                       │
│  Microsoft Fabric Eventhouse (KQL analytics)                      │
└───────────────────────────────────────────────────────────────────┘
```

### 7. Bicep Module Strategy

**Two layers that compose:**

| Layer | Source | Modules |
|-------|--------|---------|
| Platform (Avalanche) | Avalanche scaffold | VNet, NSGs, DNS zones, Key Vault, Log Analytics, App Insights, Container Apps Environment |
| Application (Aspire) | `azd infra synth` | Container Apps (api, worker, frontend), ACR, identities, role assignments, probes |

Avalanche outputs → Aspire inputs via well-known parameters:
- `managedEnvironmentId`
- `KeyVaultName`
- `LogAnalyticsWorkspaceId`
- ACR resource id

Use Aspire's `RunAsExisting` / `PublishAsExisting` to bind to Avalanche-provisioned resources without duplication.

### 8. OpenTelemetry for MAF

MAF emits OTel traces following GenAI semantic conventions via `.UseOpenTelemetry()`:

| Span | Source | Attributes |
|------|--------|-----------|
| `invoke_agent <name>` | Agent run | agent name, duration |
| `chat <model>` | Model call | model, tokens (input/output), finish reason |
| `execute_tool <name>` | Tool invocation | tool name, duration |

Activity sources to subscribe: `*Microsoft.Extensions.AI`, `*Microsoft.Extensions.Agents*`.

Aspire 13.2's **GenAI visualizer** auto-detects these and renders chat exchanges with markdown preview in the dashboard.

Custom metrics for banking FinOps:
```csharp
var meter = new Meter("AgenticWorkforce.Cost");
var costCounter = meter.CreateCounter<double>("llm.cost.usd", "USD", "Cumulative LLM cost");
var tokenCounter = meter.CreateCounter<long>("llm.tokens.total", "tokens", "Total tokens");

// In BudgetEnforcingChatClient:
costCounter.Add(cost, new("project_id", projectId), new("agent", agentName), new("model", model));
tokenCounter.Add(inputTokens + outputTokens, new("direction", "total"), new("model", model));
```

## Avalanche Integration Pattern

```
Avalanche scaffold → generates:
  ├── .azuredevops/ pipelines (deploy, PR validation, cleanup)
  ├── infra/platform/ Bicep (VNet, KV, Log Analytics, ACA Environment)
  ├── src/AppHost/ (Aspire orchestrator)
  ├── src/ServiceDefaults/
  └── src/BFF/ (starter API)

We then add:
  ├── src/Worker/ project
  ├── src/Agents/ project (MAF agents, tools, middleware)
  ├── src/Domain/ project (entities, enums, interfaces)
  ├── src/Infrastructure/ project (EF Core, Redis, external)
  ├── frontend/ (React/Vite)
  └── Additional Bicep modules (DTS, Dynamic Sessions, Blob WORM, Event Hubs)
```

Pipeline stages: `validate-bicep` → `provision-platform` (Avalanche) → `provision-app` (`azd provision`) → `build-push-images` → `deploy-app` (`azd deploy`) → `smoke-tests`

## Top 3 Architectural Risks

1. **MAF sub-package version churn** — DurableTask, A2A, Anthropic connector are all preview; pin versions in `Directory.Packages.props`
2. **Aspire has no LTS** — only latest release is supported; plan for minor-version upgrades every 6 months
3. **Two Bicep layers (Avalanche + Aspire) must compose cleanly** — use `RunAsExisting`/`PublishAsExisting` and well-known parameter outputs to avoid duplication

## What Aspire Gives Us For Free

- OpenTelemetry traces/metrics/logs (auto-exported)
- `/health` and `/alive` endpoints
- Polly-based standard resilience (retry/timeout/circuit breaker)
- Service discovery (`https+http://api`)
- Per-Container-App managed identities + Key Vault secret wiring
- Container Apps probes from health check configuration
- Dashboard with GenAI visualizer for MAF agent traces
- PostgreSQL + Redis + Vite orchestration in local dev

## Consequences

- Separate Api + Worker Container Apps adds operational overhead but enables independent scaling and safer rollouts
- EF migrations should run as a one-shot Container App Job, not at app startup (Aspire 13.3 supports this)
- `AddStackExchangeRedis("cache")` takes a connection string, not a resource name — use `GetConnectionString("cache")`
- `WithDataVolume()` on containerised Postgres doesn't reliably persist across `azd deploy` — use Azure Flexible Server for production
- Aspire's generated Bicep and Avalanche's platform Bicep must be kept in sync — parameter contracts are the interface

### Principle Compliance

- **P14 Secure by Default:** Container Apps default to no ingress, no public access, no inter-service communication until explicitly configured. The Worker has zero external endpoints by design.
- **P15 Backend Owns All Logic:** The React frontend is a pure display layer. All business logic (agent execution, budget checks, workflow orchestration, auth decisions) runs in the BFF API or Worker.
- **P16 Single Source of Truth:** PostgreSQL is the single authoritative store for application state. Redis is a cache/transport layer only. If Redis dies, rebuild from PostgreSQL. If PostgreSQL dies, stop.
- **P17 Human Authority:** Humans (ops team) can manually override scaling decisions, force-stop any Container App, and manually trigger or cancel deployments — not just rely on KEDA auto-scaling.
- **P18 Idempotency:** Messages on the Redis Stream (XADD/XREADGROUP) are processed idempotently by the Worker. Duplicate message delivery (after pod restart) does not re-execute an already-completed workflow or agent task.
- **P19 Bounded Resource Usage:** Explicit resource limits: Container App CPU/memory caps, max replica counts, Redis memory limits, PostgreSQL connection pool sizes — declared in Aspire/Bicep, not left to platform defaults.
- **P20 Version Everything:** Bicep modules, Aspire parameter contracts between Avalanche and Aspire layers, and API contracts between BFF and Worker are all versioned with explicit backward compatibility guarantees.
- **P21 Explicit Over Implicit:** Aspire's auto-wiring (service discovery, OTel, resilience defaults) is explicitly documented as declared configuration. Each `AddServiceDefaults` behaviour is enumerated — nothing implicitly inherited.
