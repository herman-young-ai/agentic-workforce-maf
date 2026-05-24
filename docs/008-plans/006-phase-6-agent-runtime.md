# Phase 6: Agent Runtime

**Status:** Not Started
**Depends On:** Phase 5 (Real-time & Events)
**Verification:** `dotnet build` exits 0; unit tests construct an agent from a catalog entry and execute it via `StubChatClient`; architecture tests assert module boundaries.

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from the previous phase. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify the previous phase's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0
4. Verify MAF NuGet package IDs and current stable versions against [https://www.nuget.org](https://www.nuget.org) before committing `Directory.Packages.props`. The Microsoft Agent Framework has been through naming churn; the IDs in §10 of this plan must be validated, not copy-pasted from memory.

---

## Objective

Implement the core agent execution infrastructure in `AgenticWorkforce.Agents/`. This is the MAF integration layer — it wraps MAF's sealed `ChatClientAgent` behind our stable interfaces, builds agents from database-driven catalog entries, assembles multi-layer prompts, registers tools, enforces budgets, and tracks costs. After this phase, the Worker can execute an agent on a task.

The Agents project depends **only on Domain** (per the layer graph in AGENTS.md). It never references EF Core, Npgsql, or `AppDbContext` directly — all persistence flows through Domain repository interfaces.

---

## Phase Split

Phase 6 lands as five sub-deliveries, each gated by green build + green tests + no CQI regression. Architecture tests (§12) are introduced in 6a and grow as later sub-phases add types.

| Sub-phase | Scope | Files | Verification |
|-----------|-------|-------|--------------|
| **6a** | Runtime + Factory + ChatClientFactory + StubChatClient + Architecture.Tests project + Agents.Tests.Unit project + DI bootstrap | ~12 | Worker resolves `IAgentRuntime`; `StubChatClient` returns a canned response. |
| **6b** | PromptAssembler + Organization/Category prompts (sourced per §3.3) + tests | ~10 | Assembler produces a 5-layer Instructions string from a catalog entry. |
| **6c** | ToolRegistry + ToolBinding + ToolRegistration + tests | ~5 | Registry resolves a manifest; unregistered tool throws; Platform vs Sandbox domain enforced. |
| **6d** | `BudgetService` impl + middleware pipeline (Budget, Audit, CostTracking, ContentSafety stub) + `ITokenCounter` + `IModelPricingService` + `ILlmCallRepository` + LlmCallDrainService + tests | ~14 | Per-iteration `LlmCall` rows persisted (including cache tokens); `BudgetEnforcingChatClient` throws on overrun. |
| **6e** | ContextAssembler + ContextPacket + ProjectContextProvider + tests | ~6 | Assembler builds priority-ordered context packet within token budget. |

`ApprovalRequiredAIFunction` is deferred to Phase 8 (see §4.3).

### Hard runtime prerequisites this phase must ship

These are not optional. They are interfaces in Domain today with **no implementation** — every middleware in §5 fails to resolve at runtime without them:

- `IBudgetService` — interface exists in `Domain/Interfaces/Services/IBudgetService.cs`; no concrete class anywhere in the solution. Ship `BudgetService` (Infrastructure) in **6d** alongside the middleware that consumes it. See §8.4.
- `IModelPricingService`, `IModelPricingRepository`, `ILlmCallRepository`, `ITokenCounter` — new Domain abstractions introduced in this phase; implementations in Infrastructure (§7, §8).
- `IMemoryCache` — single `services.AddMemoryCache()` call in `AddInfrastructure` (not Agents). Both the catalog decorator (§7) and `ChatClientFactory` (§2) consume it.

---

## Architecture (from 007-agent-implementation.md)

```
IAgentRuntime.ExecuteAsync(AgentExecutionRequest)                    ← Domain contract (unchanged)
    │
    ▼
AgentRuntime → resolves catalog entry → builds AgentExecutionContext
             → calls AgentFactory
    │
    ▼
AgentFactory (6-step construction)
    ├── Step 1: ChatClientFactory.GetOrCreate(provider, model) → shared IChatClient pipeline
    ├── Step 2: PromptAssembler.Assemble() → 5-layer Instructions string
    ├── Step 3: ToolRegistry.Resolve(manifest) → IList<AITool>
    ├── Step 4: McpToolResolver (if MCP servers in manifest)
    ├── Step 5: ProjectContextProviderFactory.Create() → per-turn context injection
    └── Step 6: new ChatClientAgent(client, options)
    │
    ▼
agent.RunAsync(objective, session) → MAF handles tool loop internally
    │
    ▼
IChatClient pipeline (per call, including each tool-loop iteration):
    ├── BudgetEnforcingChatClient.Pre  (outermost — reject if budget exhausted)
    ├── FunctionInvokingChatClient     (MAF built-in — drives the tool loop)
    │     ├── (each iteration crosses the boundary below)
    │     ├── AuditingChatClient            (non-blocking channel write per iteration)
    │     ├── CostTrackingChatClient        (one LlmCall row per iteration)
    │     ├── BudgetEnforcingChatClient.Post (record spend per iteration)
    │     ├── ContentSafetyChatClient       (content safety checks)
    │     ├── OpenTelemetry                 (wraps raw provider call)
    │     └── Raw provider                  (Foundry / Azure OpenAI / StubChatClient)
```

**Rationale for split Budget client.** Per-call cost tracking and per-iteration spend recording require those middlewares to sit **inside** `FunctionInvokingChatClient`; an aggregating budget pre-check still needs to sit **outside** so a project that's already exhausted never starts the tool loop. `BudgetEnforcingChatClient` is registered twice in the pipeline (once outside, once inside) and uses a `BudgetClientMode` ctor flag (`PreCheckOnly` vs `RecordSpend`) to switch behaviour.

---

## 1. Project Structure

```
src/AgenticWorkforce.Agents/
├── AgenticWorkforce.Agents.csproj
├── DependencyInjection.cs
│
├── Runtime/
│   ├── AgentRuntime.cs                       (implements Domain IAgentRuntime)
│   ├── AgentExecutionContext.cs              (Agents-internal value object — distinct from Domain.Entities.ProjectContext)
│   ├── IAgentFactory.cs
│   ├── AgentFactory.cs
│   ├── IChatClientFactory.cs
│   ├── ChatClientFactory.cs
│   └── StubChatClient.cs
│
├── Prompts/
│   ├── IPromptAssembler.cs
│   ├── PromptAssembler.cs
│   ├── Organization/                         (build-time generated from docs/003-principles — see §3.3)
│   └── Categories/
│       ├── project.md
│       ├── software.md
│       ├── research.md
│       ├── security.md
│       └── system.md
│
├── Tools/
│   ├── IToolRegistry.cs
│   ├── ToolRegistry.cs
│   ├── ToolBinding.cs                        (Name, McpServer, RequiresApproval, Domain)
│   └── ToolRegistration.cs
│
├── Context/
│   ├── IProjectContextProviderFactory.cs
│   ├── ProjectContextProviderFactory.cs
│   ├── ProjectContextProvider.cs
│   ├── IContextAssembler.cs
│   ├── ContextAssembler.cs
│   └── ContextPacket.cs
│
├── Middleware/
│   ├── BudgetEnforcingChatClient.cs
│   ├── AuditingChatClient.cs
│   ├── CostTrackingChatClient.cs
│   └── ContentSafetyChatClient.cs
│
└── Services/
    └── LlmCallDrainService.cs                (HostedService — drains Channel<LlmCall> via ILlmCallRepository)
```

**Not created in this phase (deferred):**

- `Tools/FileScopePolicy.cs`, `Tools/IFileScopedTool.cs` — **deleted from scope**. Principle 22 (Container-First) makes the OS-level sandbox boundary the enforcement; reintroducing a logical FileScope guard recreates exactly the BFA flaw P22 was written to eliminate. If finer-grained scoping is needed inside the sandbox, do it via the Dynamic Sessions container mount configuration, not via C# wrappers.
- `Tools/ApprovalRequiredAIFunction.cs` — **deferred to Phase 8** because it requires `IWorkflowEngine.SubmitHumanInputAsync`, which Phase 8 introduces.
- `Catalog/IAgentCatalogResolver.cs`, `Catalog/AgentCatalogResolver.cs` — **deleted from scope**. Duplicates `IAgentCatalogRepository`. Caching is implemented as a Decorator over the existing repository in Infrastructure (see §7).

---

## 2. Runtime Layer

### Domain contract (existing — unchanged)

`AgenticWorkforce.Domain.Interfaces.Services.IAgentRuntime` already defines:

```csharp
public interface IAgentRuntime
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default);
}

public record AgentExecutionRequest(
    Guid ProjectId, Guid TaskId, string AgentName, string Objective,
    string? Input = null, Guid? SessionId = null, TimeSpan? Timeout = null);

public record AgentExecutionResult(
    bool Success, string? Output, string? Error,
    long InputTokens, long OutputTokens, decimal CostUsd,
    double DurationSeconds, int ToolCallCount);
```

**This phase implements that interface as-is.** No new `AgentResult` / `AgentRunOptions` / `AgentStreamEvent` types are introduced. Streaming is deferred until a concrete caller needs it; when added, it lives on a new `IStreamingAgentRuntime` interface in Domain, not as a method overload that doubles the surface.

### AgentExecutionContext (Agents-internal value object)

A distinct value object for runtime context — named to avoid collision with `Domain.Entities.ProjectContext` (the PCD entity):

```csharp
internal sealed record AgentExecutionContext(
    Guid ProjectId,
    Guid TaskId,
    Guid? SessionId,
    string AgentName,
    string Objective,
    string? Input);
```

### AgentRuntime

```csharp
internal sealed class AgentRuntime(
    IAgentCatalogRepository catalog,
    IProjectAgentRepository projectAgents,
    IAgentFactory factory,
    TimeProvider clock,
    ILogger<AgentRuntime> logger) : IAgentRuntime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request, CancellationToken ct = default)
    {
        var entry = await catalog.GetByNameAsync(request.AgentName, ct)
            ?? throw new NotFoundException("Agent", request.AgentName);

        if (string.IsNullOrWhiteSpace(entry.AgentVersion))
            throw new InvalidStateException(
                $"AgentCatalog '{entry.AgentName}' is missing AgentVersion. Catalog rows must declare a version.");

        var projectAgentsForProject = await projectAgents.ListByProjectAsync(request.ProjectId, ct);
        var projectAgent = projectAgentsForProject.FirstOrDefault(pa => pa.AgentCatalogId == entry.Id);

        var execContext = new AgentExecutionContext(
            request.ProjectId, request.TaskId, request.SessionId,
            request.AgentName, request.Objective, request.Input);

        var timeout = request.Timeout ?? DefaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var agent = factory.Create(entry, execContext, projectAgent);
        var session = await agent.CreateSessionAsync(cts.Token);
        var start = clock.GetTimestamp();

        logger.LogInformation(
            "Executing agent {AgentName} v{AgentVersion} for project {ProjectId} task {TaskId}",
            entry.AgentName, entry.AgentVersion, request.ProjectId, request.TaskId);

        try
        {
            var response = await agent.RunAsync(
                FormatObjective(request.Objective, request.Input), session, ct: cts.Token);

            var (input, output) = ExtractTokens(response);
            return new AgentExecutionResult(
                Success: true,
                Output: response.Text,
                Error: null,
                InputTokens: input,
                OutputTokens: output,
                CostUsd: ExtractCost(response),
                DurationSeconds: clock.GetElapsedTime(start).TotalSeconds,
                ToolCallCount: CountToolCalls(response));
        }
        catch (BudgetExceededException) { throw; }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new AgentExecutionResult(false, null, "Execution timed out", 0, 0, 0, timeout.TotalSeconds, 0);
        }
    }
}
```

**Note.** The exact MAF session/thread API (`CreateSessionAsync`, `GetNewThreadAsync`, or current equivalent) and `ChatResponse.Text` / `UsageDetails` / tool-call extraction must be validated against the installed MAF assembly during 6a — the API surface has shifted between previews.

### AgentFactory (6-step construction)

Implements the exact pattern from `007-agent-implementation.md §3.2`. Key decisions:

- `UseProvidedChatClientAsIs = true` — prevents MAF from re-wrapping our pipeline.
- `RequirePerServiceCallChatHistoryPersistence = true` — saves intermediate tool results.
- Tools resolved from explicit manifest (empty manifest = zero tools — Principle 14: Secure by Default).
- Sandbox isolation enforced at the OS-level container boundary (Principle 22), not via FileScope.

### ChatClientFactory (bounded shared pipelines)

One `IChatClient` pipeline per `(provider, model)` pair. Cache is **bounded** to satisfy Principle 19:

```csharp
internal sealed class ChatClientFactory : IChatClientFactory
{
    private const int MaxCachedPipelines = 32;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxCachedPipelines });

    public IChatClient GetOrCreate(string provider, string model)
    {
        return _cache.GetOrCreate($"{provider}:{model}", entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            return BuildPipeline(provider, model);
        })!;
    }
}
```

For Phase 6, the "raw" provider is `StubChatClient` (canned response + token usage). Real Foundry / Azure OpenAI integration lands when credentials are provisioned; the pipeline shape is unchanged.

```csharp
internal sealed class StubChatClient : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        return new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "This is a stub response. Connect a real LLM provider to enable agent execution."))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
        };
    }
    // ... streaming stub
}
```

---

## 3. Prompt Assembly

### 3.1 PromptAssembler

Loads embedded markdown resources at construction, assembles 5 layers per agent:

| Layer | Source | Trim? |
|-------|--------|-------|
| 1. Organization | `Prompts/Organization/*.md` (embedded, generated — see §3.3) | Never |
| 2. Category | `Prompts/Categories/{category}.md` (embedded) | Never |
| 3. Agent system prompt | `AgentCatalog.SystemPrompt` (via `IAgentCatalogRepository`) | Never |
| 4. Project brief | `Project.Brief` (via `IProjectRepository`) | Never |
| 5. User prompt | `ProjectAgent.UserPrompt` (via `IProjectAgentRepository`) | Never |

Layers 1–5 become the `Instructions` string on `ChatClientAgentOptions`. They are static per agent construction.

**Per-turn dynamic context** (PCD, learnings, task inputs, code map, history) is injected via `AIContextProvider`, not through Instructions.

### 3.2 Category Prompts (embedded resources)

Per-category identity and constraints (~100 lines each), authored directly in `Prompts/Categories/`:

- `project.md`, `software.md`, `research.md`, `security.md`, `system.md`

### 3.3 Organization Prompts — single source of truth

The organization layer (architectural principles, security posture, communication style, output format) **must not** be free-standing duplicates of the canonical principles in `docs/003-principles/`. Two copies will drift.

**Chosen approach: generate at build time.** A small MSBuild target in `AgenticWorkforce.Agents.csproj` runs before `BeforeCompile` and synthesises `Prompts/Organization/principles.md` from `docs/003-principles/001-architectural-principles.md` (and analogous files for coding standards from `docs/005-standards/`, security posture from `docs/005-standards/06-security-coding-standards.md`). Generated files are gitignored under `Prompts/Organization/`. The target fails the build if a source file is missing.

This makes `docs/` the authoritative source — agents always run against the same principles humans read.

---

## 4. Tool Registry

### 4.1 ToolBinding (value object)

```csharp
public record ToolBinding(
    string Name,
    string? McpServer = null,
    bool RequiresApproval = false,
    ExecutionDomain Domain = ExecutionDomain.Sandbox);   // Principle 14: defaults closed

public enum ExecutionDomain { Platform, Sandbox }
```

### 4.2 Domain enforcement

`ToolRegistry` validates at registration that:

1. Every tool declares an `ExecutionDomain`.
2. Tools without an explicit domain default to `Sandbox` (Principle 14).
3. `Platform`-domain tools must implement a marker interface `IPlatformTool` and are scanned by the architecture test in §12 to assert they do not transitively depend on `HttpClient`, `System.IO.File`, or `Process`.

There is no `FileScopePolicy`. Sandbox isolation lives in the Dynamic Sessions container (Phase 7), not in the AIFunction wrapper.

### 4.3 ApprovalRequiredAIFunction — deferred to Phase 8

The approval-wrapped AIFunction depends on `IWorkflowEngine.SubmitHumanInputAsync`, which Phase 8 introduces. Shipping a stub in Phase 6 that silently no-ops, or that throws `NotImplementedException` from production code paths, both fail Principle 8 ("fail fast" yes, but only on documented missing config — not on a wrapper that exists but doesn't work).

**Plan of record:** `ApprovalRequiredAIFunction` is added in Phase 8 alongside the workflow engine. Tools marked `RequiresApproval = true` in Phase 6 register, but `ToolRegistry.Resolve` throws `InvalidStateException("Approval-required tools require Phase 8 IWorkflowEngine")` if any resolved tool has `RequiresApproval = true`. No Phase 6 agent manifest sets that flag.

---

## 5. IChatClient Middleware

### 5.0 ChatOptions tagging (how middleware reads context)

Middleware needs `(ProjectId, SessionId, TaskId, AgentName, AgentRole, Provider, RequestId)` per call. These are not part of MAF's `ChatOptions` schema, so `ChatClientFactory` tags `ChatOptions.AdditionalProperties` with well-known keys when constructing the pipeline. Agents-side code never writes to `AdditionalProperties` directly — a small `ChatOptionsTagger` helper in `Runtime/` owns the keys:

```csharp
internal static class ChatOptionsKeys
{
    public const string ProjectId  = "awp.projectId";
    public const string SessionId  = "awp.sessionId";
    public const string TaskId     = "awp.taskId";
    public const string AgentName  = "awp.agentName";
    public const string AgentRole  = "awp.agentRole";
    public const string Provider   = "awp.provider";
    public const string RequestId  = "awp.requestId";
}
```

Each middleware reads via `options.AdditionalProperties.TryGetValue(...)` and throws `InvalidStateException` on missing required keys. The architecture test in §12 asserts these constants are referenced only from the four middleware classes and `ChatClientFactory`.

### 5.1 BudgetEnforcingChatClient (registered twice)

```csharp
internal enum BudgetClientMode { PreCheckOnly, RecordSpend }

internal sealed class BudgetEnforcingChatClient(
    IChatClient inner,
    IBudgetService budgets,
    IModelPricingService pricing,
    ITokenCounter tokens,
    BudgetClientMode mode) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var ctx = ChatOptionsTagger.Read(options);   // throws if required tags missing
        var model = options?.ModelId ?? throw new InvalidStateException("ChatOptions.ModelId is required");

        if (mode == BudgetClientMode.PreCheckOnly)
        {
            var estimatedInputTokens = await tokens.CountAsync(SerializeMessages(messages), model, ct);
            var estimatedCost = await pricing.EstimateInputCostAsync(model, estimatedInputTokens, ct);
            if (!await budgets.CanSpendAsync(ctx.ProjectId, ctx.SessionId, estimatedCost, ct))
                throw new BudgetExceededException("project", ctx.ProjectId.ToString(), 0);
            return await base.GetResponseAsync(messages, options, ct);
        }

        var response = await base.GetResponseAsync(messages, options, ct);
        var usage = response.Usage;
        var actualCost = await pricing.CalculateCostAsync(
            model,
            input: usage?.InputTokenCount ?? 0,
            output: usage?.OutputTokenCount ?? 0,
            cacheRead: ReadCacheTokens(usage, "CacheReadInputTokens"),
            cacheCreate: ReadCacheTokens(usage, "CacheCreationInputTokens"),
            ct);
        await budgets.RecordSpendAsync(ctx.ProjectId, ctx.SessionId, ctx.TaskId, actualCost, ct);
        return response;
    }
}
```

`ReadCacheTokens` reads from `UsageDetails.AdditionalCounts` (Claude surfaces cache hits there; OpenAI returns 0).

### 5.2 CostTrackingChatClient (per-iteration)

Sits **inside** `FunctionInvokingChatClient` so it records one row per provider call, not per agent turn. Captures **all four token classes** (input, output, cache-read, cache-create) so Claude cache savings are accurately reflected:

```csharp
internal sealed class CostTrackingChatClient(
    IChatClient inner,
    IModelPricingService pricing,
    ChannelWriter<LlmCall> llmCallWriter,
    TimeProvider clock) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var ctx = ChatOptionsTagger.Read(options);
        var model = options?.ModelId ?? throw new InvalidStateException("ChatOptions.ModelId is required");

        var start = clock.GetTimestamp();
        var response = await base.GetResponseAsync(messages, options, ct);
        var elapsed = clock.GetElapsedTime(start);

        var usage = response.Usage;
        var inputTokens   = usage?.InputTokenCount ?? 0;
        var outputTokens  = usage?.OutputTokenCount ?? 0;
        var cacheRead     = ReadCacheTokens(usage, "CacheReadInputTokens");
        var cacheCreate   = ReadCacheTokens(usage, "CacheCreationInputTokens");

        var cost = await pricing.CalculateCostAsync(model, inputTokens, outputTokens, cacheRead, cacheCreate, ct);

        var record = new LlmCall
        {
            ProjectId           = ctx.ProjectId,
            SessionId           = ctx.SessionId,
            TaskId              = ctx.TaskId,
            AgentName           = ctx.AgentName,
            AgentRole           = ctx.AgentRole,
            Model               = model,
            Provider            = ctx.Provider,
            InputTokens         = inputTokens,
            OutputTokens        = outputTokens,
            CacheReadTokens     = cacheRead,
            CacheCreationTokens = cacheCreate,
            CostUsd             = cost,
            LatencyMs           = (int)elapsed.TotalMilliseconds,
            RequestId           = ctx.RequestId,
            ToolCount           = CountTools(response)
        };

        if (!llmCallWriter.TryWrite(record))
            throw new AuditBackpressureException("LlmCall channel full");

        return response;
    }
}
```

### 5.3 AuditingChatClient

Writes audit records to a `Channel<AuditRecord>` (Phase 9 drains this to Event Hubs + WORM). For Phase 6, the channel is consumed by `LlmCallDrainService` which persists via `ILlmCallRepository`.

### 5.4 ContentSafetyChatClient

Pass-through in Phase 6 (no Azure AI Content Safety integration yet). Stub exists so the pipeline shape is complete; replaced when content safety is provisioned.

---

## 6. Context Assembly

### ContextAssembler

Priority-based token budget allocation per ADR-010. **Tokenisation is delegated to `ITokenCounter`** — no inline estimation:

```csharp
internal sealed class ContextAssembler(
    IProjectContextService pcdService,
    ILearningRepository learnings,
    ITokenCounter tokens) : IContextAssembler
{
    private const int DefaultBudgetTokens = 100_000;

    public async Task<ContextPacket> BuildAsync(
        Guid projectId, string? taskDefinition,
        AgentCatalog agent, string[]? domainTags,
        string model, CancellationToken ct)
    {
        var budget = new TokenBudget(DefaultBudgetTokens);
        var messages = new List<ChatMessage>();

        // Priority 0: PCD (NEVER trimmed)
        var pcd = await pcdService.GetAsync(projectId, ct);
        var pcdMsg = $"## Project Context\n\n{pcd.ContextData}";
        messages.Add(ChatMessage.CreateSystemMessage(pcdMsg));
        budget.Consume(await tokens.CountAsync(pcdMsg, model, ct));

        // Priority 1a: Task definition (NEVER trimmed)
        if (taskDefinition != null)
        {
            var taskMsg = $"## Current Task\n\n{taskDefinition}";
            messages.Add(ChatMessage.CreateSystemMessage(taskMsg));
            budget.Consume(await tokens.CountAsync(taskMsg, model, ct));
        }

        // Priority 2.5: Active learnings (top by confidence, skipped if budget tight)
        if (budget.Remaining > 10_000)
        {
            var learningsPage = await learnings.ListByProjectPagedAsync(
                projectId, new PagedQuery(1, 20), ct);
            var active = learningsPage.Items
                .Where(l => l.Status == LearningStatus.Active)
                .OrderByDescending(l => l.Confidence)
                .ToList();
            var learningText = FormatLearnings(active, budget.Remaining / 4);
            if (learningText.Length > 0)
            {
                var msg = $"## Learnings\n\n{learningText}";
                messages.Add(ChatMessage.CreateSystemMessage(msg));
                budget.Consume(await tokens.CountAsync(msg, model, ct));
            }
        }

        // Priority 2: Execution history (trimmed FIRST) — included only if budget remains

        return new ContextPacket(messages, null, null);
    }
}
```

**No `AppDbContext`.** `ILearningRepository` already exposes `ListByProjectPagedAsync`; in-memory filtering by `Status == Active` is acceptable at 20-row scale. If filtered queries are needed later, extend the repository, don't reach past it.

---

## 7. Catalog Caching (Decorator pattern)

No new `IAgentCatalogResolver` interface. The existing `IAgentCatalogRepository` is wrapped by a Decorator in Infrastructure:

```csharp
// In src/AgenticWorkforce.Infrastructure/Repositories/CachingAgentCatalogRepository.cs
internal sealed class CachingAgentCatalogRepository(
    AgentCatalogRepository inner,
    IMemoryCache cache) : IAgentCatalogRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public Task<AgentCatalog?> GetByNameAsync(string agentName, CancellationToken ct = default) =>
        cache.GetOrCreateAsync($"agent:name:{agentName}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return inner.GetByNameAsync(agentName, ct);
        })!;

    // GetByIdAsync similarly cached
    // Write paths (AddAsync, UpdateAsync, SetEnabledAsync) evict matching keys before delegating
}
```

Registered in `AddInfrastructure`:

```csharp
services.AddScoped<AgentCatalogRepository>();
services.AddScoped<IAgentCatalogRepository>(sp =>
    new CachingAgentCatalogRepository(
        sp.GetRequiredService<AgentCatalogRepository>(),
        sp.GetRequiredService<IMemoryCache>()));
```

Single abstraction. Single read source (PostgreSQL). Cache lives in Infrastructure where the data does.

---

## 8. New Domain Abstractions and Implementations

### 8.1 ITokenCounter

```csharp
// In src/AgenticWorkforce.Domain/Interfaces/Services/ITokenCounter.cs
public interface ITokenCounter
{
    Task<int> CountAsync(string text, string modelId, CancellationToken ct = default);
}
```

Single overload — Domain stays MAF-free. Agents-side callers serialise messages to a role-marked string before counting (`"[system]\n...\n[user]\n..."`). Concrete implementations live in Infrastructure:

- `TiktokenTokenCounter` (OpenAI families — uses `Microsoft.ML.Tokenizers`).
- `AnthropicTokenCounter` (Claude — uses the Anthropic count-tokens API or a vendored BPE table).
- `TokenCounterRouter` dispatches by `modelId` prefix and is registered as `ITokenCounter` in DI.

Throws `InvalidStateException` if `modelId` matches no router branch — never falls back to a default tokenizer (different tokenizers give different counts; silent substitution would corrupt budget math).

### 8.2 IModelPricingService

```csharp
// In src/AgenticWorkforce.Domain/Interfaces/Services/IModelPricingService.cs
public interface IModelPricingService
{
    Task<decimal> EstimateInputCostAsync(string model, int inputTokens, CancellationToken ct = default);

    Task<decimal> CalculateCostAsync(
        string model,
        long input,
        long output,
        long cacheRead,
        long cacheCreate,
        CancellationToken ct = default);
}
```

Signature carries all four token classes because `ModelPricing` has separate `PricePerMtokCacheRead` and `PricePerMtokCacheCreate` columns — ignoring them would over- or under-charge Claude calls. Token counts are `long` to match `LlmCall` storage.

Backed by `IModelPricingRepository` over the existing `ModelPricing` entity. Resolves rate by `(model, EffectiveFrom <= now < EffectiveTo)`. Fails fast with `InvalidStateException` if no row matches (Principle 8 — no silent fallback to a hardcoded rate).

### 8.3 ILlmCallRepository

```csharp
// In src/AgenticWorkforce.Domain/Interfaces/Repositories/ILlmCallRepository.cs
public interface ILlmCallRepository
{
    Task AddBatchAsync(IReadOnlyList<LlmCall> calls, CancellationToken ct = default);
}
```

`LlmCallDrainService` batches up to 500 records or 5 seconds (whichever first) and calls `AddBatchAsync`. Single bulk insert per batch. Existing `CostQueryService` (read-side aggregator over the same table) is unchanged.

### 8.4 BudgetService (new Infrastructure implementation of existing Domain interface)

`IBudgetService` exists in Domain today but has **no implementation** — `services.AddScoped<IBudgetService, …>` is not registered anywhere in `AddInfrastructure`. Without it, `BudgetEnforcingChatClient` will fail at first resolve in the Worker DI container.

This phase ships `BudgetService` in `src/AgenticWorkforce.Infrastructure/Services/BudgetService.cs`:

```csharp
internal sealed class BudgetService(
    AppDbContext db,
    TimeProvider clock,
    IEventPublisher events,
    ILogger<BudgetService> logger) : IBudgetService
{
    private const decimal WarnThreshold = 0.80m;

    public async Task<bool> CanSpendAsync(
        Guid projectId, Guid? sessionId, decimal estimatedCostUsd, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(projectId, ct);
        if (status.IsExhausted) return false;
        return status.RemainingUsd >= estimatedCostUsd;
    }

    public async Task RecordSpendAsync(
        Guid projectId, Guid? sessionId, Guid? taskId, decimal costUsd, CancellationToken ct = default)
    {
        // Spend is the sum of LlmCall.CostUsd rows; no separate counter row.
        // CostTrackingChatClient already wrote the LlmCall row. This method
        // exists to (a) emit a budget-warning event at 80% and (b) give a
        // single seam to switch to an explicit budget-counter table later if
        // sum-over-LlmCalls becomes a hotspot.
        var status = await GetStatusAsync(projectId, ct);
        if (status.CeilingUsd > 0 && status.UsedUsd / status.CeilingUsd >= WarnThreshold)
            await events.PublishAsync(/* BudgetWarning event */, ct);
    }

    public async Task<BudgetStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId.ToString());

        var ceiling = project.BudgetCeilingUsd ?? 0m;
        var used = await db.LlmCalls.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m;
        var remaining = Math.Max(0m, ceiling - used);
        return new BudgetStatus(ceiling, used, remaining, ceiling > 0 && used >= ceiling);
    }
}
```

Source of truth is `LlmCall.CostUsd` — single read source, no separate counter (Principle 16). The sum-over-table approach is fine at current scale because `LlmCall` is partitioned by `(project_id, created_at)`. If aggregation latency becomes a hotspot, introduce a materialised running-total in a follow-up phase — do not introduce it speculatively now.

---

## 9. DependencyInjection.cs

```csharp
public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        // Runtime
        services.AddScoped<IAgentRuntime, AgentRuntime>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // Prompts
        services.AddSingleton<IPromptAssembler, PromptAssembler>();

        // Tools
        services.AddAgentTools(); // from ToolRegistration.cs

        // Context
        services.AddScoped<IProjectContextProviderFactory, ProjectContextProviderFactory>();
        services.AddScoped<IContextAssembler, ContextAssembler>();

        // Middleware channels (bounded — Principle 19)
        services.AddSingleton(Channel.CreateBounded<LlmCall>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.Wait }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Writer);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Reader);
        services.AddHostedService<LlmCallDrainService>();

        return services;
    }
}
```

`IAgentCatalogRepository` caching, `ITokenCounter`, `IModelPricingService`, and `ILlmCallRepository` implementations are registered in `AgenticWorkforce.Infrastructure/DependencyInjection.cs`, not here — Agents must not know how persistence is wired.

---

## 10. Worker Integration

Update `src/AgenticWorkforce.Worker/Program.cs`:

```csharp
builder.Services.AddAgentServices();
```

---

## 11. Package Additions

```xml
<!-- Verify each ID and the current stable version on NuGet before committing -->
<PackageVersion Include="Microsoft.Extensions.AI" Version="?" />
<PackageVersion Include="Microsoft.Extensions.AI.Agents" Version="?" />
<PackageVersion Include="Microsoft.Extensions.AI.AzureAIInference" Version="?" />
<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="?" />
<PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="?" />
<PackageVersion Include="NetArchTest.Rules" Version="?" />
<PackageVersion Include="Microsoft.ML.Tokenizers" Version="?" />
```

Package IDs marked above must be confirmed against [https://www.nuget.org](https://www.nuget.org) before the build runs. The MAF package family has shipped under multiple IDs across previews; do not guess from memory.

---

## 12. Architecture Tests

A new test project `tests/AgenticWorkforce.Architecture.Tests/` is created in **6a** and asserts:

```csharp
public class ModuleBoundaryTests
{
    [Fact]
    public void Domain_HasNoExternalDependencies()
    {
        var result = Types.InAssembly(typeof(IAgentRuntime).Assembly)
            .Should().NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Npgsql",
                "Microsoft.Extensions.AI", "Microsoft.Extensions.AI.Agents",
                "StackExchange.Redis", "Azure.Storage")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Agents_HasNoEfCoreOrNpgsqlDependency()
    {
        var result = Types.InAssembly(typeof(AgentRuntime).Assembly)
            .Should().NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Api_HasNoMafOrEfCoreLeakage()
    {
        // Api may reference EF Core for DI only — assert no application code
        // imports Microsoft.EntityFrameworkCore.* or Microsoft.Extensions.AI.*
    }

    [Fact]
    public void PlatformTools_DoNotUseHttpOrFile()
    {
        var result = Types.InAssembly(typeof(AgentRuntime).Assembly)
            .That().ImplementInterface(typeof(IPlatformTool))
            .Should().NotHaveDependencyOnAny("System.Net.Http", "System.IO.File", "System.Diagnostics.Process")
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void NoDateTimeNowUsage()
    {
        // Compile-time check via Roslyn analyzer or source scan
    }
}
```

Architecture tests run in CI on every PR.

---

## File Summary

### Files to CREATE (Agents project + Domain abstractions)

```
# Agents project (~28 files in Phase 6)
src/AgenticWorkforce.Agents/DependencyInjection.cs
src/AgenticWorkforce.Agents/Runtime/AgentRuntime.cs
src/AgenticWorkforce.Agents/Runtime/AgentExecutionContext.cs
src/AgenticWorkforce.Agents/Runtime/IAgentFactory.cs
src/AgenticWorkforce.Agents/Runtime/AgentFactory.cs
src/AgenticWorkforce.Agents/Runtime/IChatClientFactory.cs
src/AgenticWorkforce.Agents/Runtime/ChatClientFactory.cs
src/AgenticWorkforce.Agents/Runtime/StubChatClient.cs
src/AgenticWorkforce.Agents/Prompts/IPromptAssembler.cs
src/AgenticWorkforce.Agents/Prompts/PromptAssembler.cs
src/AgenticWorkforce.Agents/Prompts/Categories/project.md
src/AgenticWorkforce.Agents/Prompts/Categories/software.md
src/AgenticWorkforce.Agents/Prompts/Categories/research.md
src/AgenticWorkforce.Agents/Prompts/Categories/security.md
src/AgenticWorkforce.Agents/Prompts/Categories/system.md
src/AgenticWorkforce.Agents/Tools/IToolRegistry.cs
src/AgenticWorkforce.Agents/Tools/ToolRegistry.cs
src/AgenticWorkforce.Agents/Tools/ToolBinding.cs
src/AgenticWorkforce.Agents/Tools/IPlatformTool.cs
src/AgenticWorkforce.Agents/Tools/ToolRegistration.cs
src/AgenticWorkforce.Agents/Context/IProjectContextProviderFactory.cs
src/AgenticWorkforce.Agents/Context/ProjectContextProviderFactory.cs
src/AgenticWorkforce.Agents/Context/ProjectContextProvider.cs
src/AgenticWorkforce.Agents/Context/IContextAssembler.cs
src/AgenticWorkforce.Agents/Context/ContextAssembler.cs
src/AgenticWorkforce.Agents/Context/ContextPacket.cs
src/AgenticWorkforce.Agents/Middleware/BudgetEnforcingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/AuditingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/CostTrackingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/ContentSafetyChatClient.cs
src/AgenticWorkforce.Agents/Services/LlmCallDrainService.cs

# Domain abstractions (new)
src/AgenticWorkforce.Domain/Interfaces/Services/ITokenCounter.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IModelPricingService.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IModelPricingRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/ILlmCallRepository.cs

# Infrastructure implementations (new)
src/AgenticWorkforce.Infrastructure/Repositories/CachingAgentCatalogRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/ModelPricingRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/LlmCallRepository.cs
src/AgenticWorkforce.Infrastructure/Services/TiktokenTokenCounter.cs
src/AgenticWorkforce.Infrastructure/Services/AnthropicTokenCounter.cs
src/AgenticWorkforce.Infrastructure/Services/TokenCounterRouter.cs
src/AgenticWorkforce.Infrastructure/Services/ModelPricingService.cs
src/AgenticWorkforce.Infrastructure/Services/BudgetService.cs              # implements existing Domain IBudgetService

# New test projects
tests/AgenticWorkforce.Agents.Tests.Unit/AgenticWorkforce.Agents.Tests.Unit.csproj
tests/AgenticWorkforce.Agents.Tests.Unit/Runtime/AgentRuntimeTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Runtime/ChatClientFactoryTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Prompts/PromptAssemblerTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Tools/ToolRegistryTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Middleware/BudgetEnforcingChatClientTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Middleware/CostTrackingChatClientTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Context/ContextAssemblerTests.cs
tests/AgenticWorkforce.Architecture.Tests/AgenticWorkforce.Architecture.Tests.csproj
tests/AgenticWorkforce.Architecture.Tests/ModuleBoundaryTests.cs
```

### Files to MODIFY

```
AgenticWorkforce.slnx — Add Agents.Tests.Unit and Architecture.Tests projects
src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj — Add MAF packages + MSBuild target to generate Prompts/Organization/*.md from docs/003-principles/, docs/005-standards/
src/AgenticWorkforce.Agents/.gitignore — Ignore generated Prompts/Organization/*.md
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs — Register: AddMemoryCache(); IBudgetService -> BudgetService; CachingAgentCatalogRepository (decorator over existing AgentCatalogRepository); IModelPricingService -> ModelPricingService; IModelPricingRepository -> ModelPricingRepository; ILlmCallRepository -> LlmCallRepository; ITokenCounter -> TokenCounterRouter (which fans out to Tiktoken/Anthropic counters)
src/AgenticWorkforce.Worker/Program.cs — Add AddAgentServices()
Directory.Packages.props — Add MAF + tokenizer + NetArchTest package versions (validated against NuGet)
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0.
2. `dotnet test AgenticWorkforce.slnx` exits 0. New tests include:
   - `AgentRuntimeTests`: resolves catalog entry via `IAgentCatalogRepository`, constructs agent, returns `AgentExecutionResult` from `StubChatClient`. Throws `InvalidStateException` when `AgentVersion` is null.
   - `PromptAssemblerTests`: assembles 5 layers correctly; handles missing optional layers (4, 5).
   - `ToolRegistryTests`: resolves manifest; throws on unregistered tool; defaults `ExecutionDomain` to `Sandbox`; throws `InvalidStateException` if any resolved tool has `RequiresApproval = true`.
   - `BudgetEnforcingChatClientTests`: pre-check throws `BudgetExceededException` when budget exhausted; record-spend mode calls `IBudgetService.RecordSpendAsync` with `taskId`.
   - `CostTrackingChatClientTests`: writes one `LlmCall` per `GetResponseAsync` invocation; throws `AuditBackpressureException` on full channel.
   - `ContextAssemblerTests`: PCD always included; learnings skipped when budget < 10k; uses injected `ITokenCounter` (no inline estimation).
   - `ChatClientFactoryTests`: cache returns same instance for repeated `(provider, model)`; cache is bounded (eviction occurs above MaxCachedPipelines).
3. `IAgentRuntime`, `IBudgetService`, `IModelPricingService`, `ILlmCallRepository`, and `ITokenCounter` are all resolvable from the Worker DI container (a single `services.BuildServiceProvider().GetRequiredService<...>()` test verifies each — closes the gap that today `IBudgetService` has no implementation).
4. `BudgetEnforcingChatClient` throws `BudgetExceededException` when budget is exceeded; both pre-check and record-spend modes verified. `BudgetService.GetStatusAsync` computes `UsedUsd` as the sum of `LlmCall.CostUsd` (verified against a seeded project + LlmCall fixture).
5. Embedded resource prompts load correctly: Organization (generated from `docs/`) + Categories (authored).
6. **Architecture tests pass**: Domain has no Microsoft.EntityFrameworkCore / Npgsql / Microsoft.Extensions.AI references; Agents has no EF Core / Npgsql references; Platform-domain tools do not depend on `HttpClient` / `System.IO.File` / `Process`.
7. `LlmCallDrainService` background service starts and drains channel via `ILlmCallRepository.AddBatchAsync` in batches.
8. CQI score does not regress below the previous phase's baseline.

---

## Goal Command

```
/goal Agent runtime infrastructure complete in AgenticWorkforce.Agents, implementing the existing Domain IAgentRuntime contract (ExecuteAsync(AgentExecutionRequest) -> AgentExecutionResult). AgentRuntime resolves catalog via IAgentCatalogRepository and ProjectAgents via IProjectAgentRepository (no AppDbContext in Agents). AgentFactory performs 6-step construction. ChatClientFactory builds bounded shared IChatClient pipelines: BudgetEnforcing.PreCheck (outermost) -> FunctionInvocation -> [Auditing, CostTracking, BudgetEnforcing.RecordSpend, ContentSafety, OTel, Raw] per iteration. PromptAssembler composes 5 layers from generated Organization prompts (sourced at build from docs/003-principles + docs/005-standards) + authored Category prompts + DB-backed agent/project/user layers. ToolRegistry defaults ExecutionDomain to Sandbox; rejects RequiresApproval tools (deferred to Phase 8); no FileScopePolicy (Principle 22). ContextAssembler uses injected ITokenCounter for all token math. New Domain abstractions: ITokenCounter, IModelPricingService, IModelPricingRepository, ILlmCallRepository (implementations in Infrastructure). CachingAgentCatalogRepository decorator wraps the existing repository in Infrastructure. New test projects: AgenticWorkforce.Agents.Tests.Unit and AgenticWorkforce.Architecture.Tests (NetArchTest assertions for module boundaries). Worker calls AddAgentServices(). Verify: dotnet build exits 0, dotnet test exits 0 (unit + architecture tests), CQI does not regress. MAF and tokenizer package IDs verified against NuGet before committing Directory.Packages.props. Land as 6a (Runtime + Factory + Stub + Architecture.Tests), 6b (PromptAssembler + prompt generation), 6c (ToolRegistry), 6d (Middleware + ITokenCounter + IModelPricingService + ILlmCallRepository + DrainService), 6e (ContextAssembler). Stop after 40 turns per sub-phase.
```
