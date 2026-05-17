# Phase 6: Agent Runtime

**Status:** Not Started
**Depends On:** Phase 5 (Real-time & Events)
**Verification:** `dotnet build` exits 0, unit test creates agent from catalog entry and executes via mock IChatClient

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from the previous phase. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify the previous phase's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0

---

## Objective

Implement the core agent execution infrastructure in `AgenticWorkforce.Agents/`. This is the MAF integration layer — it wraps MAF's sealed `ChatClientAgent` behind our stable interfaces, builds agents from database-driven catalog entries, assembles multi-layer prompts, registers tools, enforces budgets, and tracks costs. After this phase, the Worker can execute an agent on a task.

---

## Architecture (from 007-agent-implementation.md)

```
IAgentRuntime.RunAsync("security.reviewer", objective, projectContext)
    │
    ▼
AgentRuntime → resolves catalog entry → calls AgentFactory
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
IChatClient pipeline (per call, including tool loop iterations):
    ├── BudgetEnforcingChatClient (outermost — reject if budget exceeded)
    ├── AuditingChatClient (non-blocking channel write)
    ├── CostTrackingChatClient (record to LlmCall table)
    ├── FunctionInvokingChatClient (MAF built-in — tool loop)
    ├── ContentSafetyChatClient (content safety checks)
    ├── OpenTelemetry (innermost — wraps raw provider)
    └── Raw provider (Foundry Anthropic or Azure OpenAI)
```

---

## 1. Project Structure

```
src/AgenticWorkforce.Agents/
├── AgenticWorkforce.Agents.csproj
├── DependencyInjection.cs
│
├── Runtime/
│   ├── AgentRuntime.cs
│   ├── IAgentFactory.cs
│   ├── AgentFactory.cs
│   ├── IChatClientFactory.cs
│   └── ChatClientFactory.cs
│
├── Prompts/
│   ├── IPromptAssembler.cs
│   ├── PromptAssembler.cs
│   ├── Organization/
│   │   ├── principles.md
│   │   ├── coding-standards.md
│   │   ├── security-posture.md
│   │   └── communication-style.md
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
│   ├── ToolBinding.cs
│   ├── FileScopePolicy.cs
│   ├── IFileScopedTool.cs
│   ├── ApprovalRequiredAIFunction.cs
│   └── ToolRegistration.cs
│
├── Context/
│   ├── IProjectContextProviderFactory.cs
│   ├── ProjectContextProviderFactory.cs
│   ├── ProjectContextProvider.cs
│   ├── IContextAssembler.cs
│   ├── ContextAssembler.cs
│   └── ProjectContext.cs (value object — not the entity)
│
├── Middleware/
│   ├── BudgetEnforcingChatClient.cs
│   ├── AuditingChatClient.cs
│   ├── CostTrackingChatClient.cs
│   └── ContentSafetyChatClient.cs
│
└── Catalog/
    ├── IAgentCatalogResolver.cs
    └── AgentCatalogResolver.cs
```

---

## 2. Runtime Layer

### Interface Hierarchy

- **Domain `IAgentRuntime`** (public, primitive types) — what Api and Worker resolve from DI
- **Internal `IAgentRuntimeInternal`** (internal to Agents) — richer interface with `ProjectContext` value object

The public `AgentRuntime` implements BOTH: it satisfies the Domain contract for external callers, and exposes the rich interface for internal Agents-project code (e.g., `AgentVerifier` calling `system.verifier`).

### AgentRuntime

```csharp
internal sealed class AgentRuntime(
    IAgentCatalogResolver catalog,
    IAgentFactory factory,
    AppDbContext db,
    TimeProvider clock,
    ILogger<AgentRuntime> logger) : IAgentRuntime, IAgentRuntimeInternal
{
    public async Task<AgentResult> RunAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default)
    {
        var entry = await catalog.ResolveAsync(agentName, ct)
            ?? throw new NotFoundException("Agent", agentName);

        var projectAgent = await db.ProjectAgents
            .FirstOrDefaultAsync(pa => pa.ProjectId == context.ProjectId && pa.AgentCatalogId == entry.Id, ct);
        var timeout = options?.Timeout ?? TimeSpan.FromMinutes(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var agent = factory.Create(entry, context, projectAgent, options?.PromptVariant);
        var session = await agent.CreateSessionAsync(cts.Token);
        var start = clock.GetTimestamp();

        logger.LogInformation("Executing agent {AgentName} for project {ProjectId}",
            agentName, context.ProjectId);

        var response = await agent.RunAsync(
            FormatObjective(objective, options?.Inputs), session, ct: cts.Token);

        return new AgentResult(
            Output: response.Text,
            CostUsd: ExtractCost(response),
            Duration: clock.GetElapsedTime(start),
            ToolCallCount: CountToolCalls(response),
            Tokens: ExtractTokens(response),
            AgentVersion: entry.AgentVersion ?? "1.0.0");
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Same setup as RunAsync, but uses agent.RunStreamingAsync
        // Yields AgentStreamEvent for each token chunk / tool call / completion
    }
}
```

### AgentFactory (6-step construction)

Implements the exact pattern from `007-agent-implementation.md §3.2` — see architecture doc for the full code. Key decisions:

- `UseProvidedChatClientAsIs = true` — prevents MAF from wrapping our pipeline
- `RequirePerServiceCallChatHistoryPersistence = true` — saves intermediate tool results
- Tools resolved from explicit manifest (empty manifest = zero tools)
- File scope enforced at tool level via `IFileScopedTool`

### ChatClientFactory (shared pipelines)

One `IChatClient` pipeline per `(provider, model)` pair. Cached in `ConcurrentDictionary`. Pipeline order:

```
BudgetEnforcing → Auditing → CostTracking → FunctionInvocation → ContentSafety → OTel → Raw
```

For Phase 6, the "raw" provider is a **stub** that returns canned responses. Real Foundry/Azure OpenAI integration comes when API keys are available. The architecture is wired correctly regardless.

```csharp
// Stub provider for development/testing
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

### PromptAssembler

Loads embedded markdown resources at construction, assembles 5 layers per agent:

| Layer | Source | Trim? |
|-------|--------|-------|
| 1. Organization | `Prompts/Organization/*.md` (embedded) | Never |
| 2. Category | `Prompts/Categories/{category}.md` (embedded) | Never |
| 3. Agent system prompt | `AgentCatalog.SystemPrompt` (DB) | Never |
| 4. Project brief | `Project.Brief` (DB) | Never |
| 5. User prompt | `ProjectAgent.UserPrompt` (DB) | Never |

Layers 1-5 become the `Instructions` string on `ChatClientAgentOptions`. They are static per agent construction.

**Per-turn dynamic context** (PCD, learnings, task inputs, code map, history) is injected via `AIContextProvider`, not through Instructions.

### Organization Prompts (embedded resources)

Short, stable files (~200 lines total):

- `principles.md` — top 5 architectural principles agents must follow
- `coding-standards.md` — output format requirements
- `security-posture.md` — what agents must never do (data handling, network)
- `communication-style.md` — tone, structure, response format

### Category Prompts

Per-category identity and constraints (~100 lines each):

- `project.md` — project management agent identity
- `software.md` — software engineering agent identity
- `research.md` — research agent identity
- `security.md` — security agent identity
- `system.md` — system/utility agent identity

---

## 4. Tool Registry

### ToolBinding (value object)

```csharp
public record ToolBinding(
    string Name,
    string? McpServer = null,
    bool RequiresApproval = false,
    ExecutionDomain Domain = ExecutionDomain.Sandbox);

public enum ExecutionDomain { Platform, Sandbox }
```

### FileScopePolicy

```csharp
public record FileScopePolicy(
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> DeniedPaths,
    bool AllowAll = false);
```

### ApprovalRequiredAIFunction

Wraps an `AIFunction` to pause before execution, creating a `HumanInputRequest` and waiting for approval. Used for high-impact tools (e.g., `project.run_objective`, destructive shell commands).

When the human responds, the wrapper inspects `HumanInputRequest.Decision`:
- `Approved` → invoke the wrapped function, return its result
- `Rejected` → throw `ApprovalRejectedException` with `request.Response` as the reason; the agent sees this as a tool error and can react
- `Escalated` → throw `ApprovalEscalatedException`; the agent should typically stop and let the supervisor route to a higher authority
- `Overridden` → invoke the function but tag the audit record with `decision_overridden=true` (typical when a senior reviewer overrides a policy block)

Use `IWorkflowEngine.SubmitHumanInputAsync` (Phase 8) as the single resolution point so the same enum semantics apply whether the approval comes from a workflow gate or a tool wrapper.

---

## 5. IChatClient Middleware

### BudgetEnforcingChatClient

```csharp
internal sealed class BudgetEnforcingChatClient(
    IChatClient inner, IBudgetService budgetService) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        // Extract project/session context from ChatOptions.AdditionalProperties
        var projectId = GetProjectId(options);
        var sessionId = GetSessionId(options);

        // Estimate cost (input tokens * model rate)
        var estimatedCost = EstimateCost(messages, options);

        if (!await budgetService.CanSpendAsync(projectId, sessionId, estimatedCost, ct))
            throw new BudgetExceededException("project", projectId.ToString(), 0);

        var response = await base.GetResponseAsync(messages, options, ct);

        // Record actual spend
        var actualCost = CalculateActualCost(response);
        await budgetService.RecordSpendAsync(projectId, sessionId, null, actualCost, ct);

        return response;
    }
}
```

### CostTrackingChatClient

Records every LLM call to the `LlmCall` table (via repository, async fire-and-forget with bounded channel):

```csharp
internal sealed class CostTrackingChatClient(
    IChatClient inner,
    ChannelWriter<LlmCall> llmCallWriter) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(...)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await base.GetResponseAsync(messages, options, ct);
        var elapsed = Stopwatch.GetElapsedTime(start);

        var record = new LlmCall
        {
            ProjectId = GetProjectId(options),
            SessionId = GetSessionId(options),
            TaskId = GetTaskId(options),
            AgentName = GetAgentName(options),
            Model = options?.ModelId ?? "unknown",
            Provider = GetProvider(options),
            InputTokens = response.Usage?.InputTokenCount ?? 0,
            OutputTokens = response.Usage?.OutputTokenCount ?? 0,
            CostUsd = CalculateCost(response),
            LatencyMs = (int)elapsed.TotalMilliseconds,
            ToolCount = CountTools(response)
        };

        // Non-blocking write to bounded channel
        llmCallWriter.TryWrite(record);
        return response;
    }
}
```

### AuditingChatClient

Writes audit records to a `Channel<AuditRecord>` (Phase 9 drains this to Event Hubs + WORM). For now, the channel is consumed by a simple background service that logs and discards.

### ContentSafetyChatClient

Stub in Phase 6 — passes through. Real content safety checks come when Azure AI Content Safety is integrated.

---

## 6. Context Assembly

### ContextAssembler

Priority-based token budget allocation per ADR-010:

```csharp
internal sealed class ContextAssembler(
    IProjectContextService pcdService,
    AppDbContext db,
    ITaskRepository taskRepo) : IContextAssembler
{
    private const int DefaultBudgetTokens = 100_000;

    public async Task<ContextPacket> BuildAsync(
        Guid projectId, string? taskDefinition,
        AgentCatalog agent, string[]? domainTags, CancellationToken ct)
    {
        var budget = new TokenBudget(DefaultBudgetTokens);
        var messages = new List<ChatMessage>();

        // Priority 0: PCD (NEVER trimmed)
        var pcd = await pcdService.GetAsync(projectId, ct);
        messages.Add(ChatMessage.CreateSystemMessage($"## Project Context\n\n{pcd.ContextData}"));
        budget.Consume(EstimateTokens(pcd.ContextData));

        // Priority 1a: Task definition (NEVER trimmed)
        if (taskDefinition != null)
        {
            messages.Add(ChatMessage.CreateSystemMessage($"## Current Task\n\n{taskDefinition}"));
            budget.Consume(EstimateTokens(taskDefinition));
        }

        // Priority 2.5: Learnings (skipped if budget tight)
        if (budget.Remaining > 10_000)
        {
            var learnings = await db.ProjectLearnings
                .Where(l => l.ProjectId == projectId && l.Status == LearningStatus.Active)
                .OrderByDescending(l => l.Confidence)
                .Take(20)
                .ToListAsync(ct);
            var learningText = FormatLearnings(learnings, budget.Remaining / 4);
            if (learningText.Length > 0)
            {
                messages.Add(ChatMessage.CreateSystemMessage($"## Learnings\n\n{learningText}"));
                budget.Consume(EstimateTokens(learningText));
            }
        }

        // Priority 2: Execution history (trimmed FIRST)
        // Included only if budget remains

        return new ContextPacket(messages, null, null);
    }
}
```

---

## 7. Catalog Resolver

### AgentCatalogResolver

```csharp
internal sealed class AgentCatalogResolver(
    AppDbContext db,
    IMemoryCache cache) : IAgentCatalogResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<AgentCatalog?> ResolveAsync(string agentName, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync($"agent:{agentName}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await db.AgentCatalogs
                .FirstOrDefaultAsync(a => a.AgentName == agentName && a.Enabled, ct);
        });
    }
}
```

---

## 8. DependencyInjection.cs

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

        // Catalog
        services.AddScoped<IAgentCatalogResolver, AgentCatalogResolver>();
        services.AddMemoryCache();

        // Middleware channels
        services.AddSingleton(Channel.CreateBounded<LlmCall>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.Wait }));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Writer);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<LlmCall>>().Reader);
        services.AddHostedService<LlmCallDrainService>();

        return services;
    }
}
```

---

## 9. Worker Integration

Update `src/AgenticWorkforce.Worker/Program.cs`:

```csharp
builder.Services.AddAgentServices();
```

---

## File Summary

### Files to CREATE (~35 files)

```
src/AgenticWorkforce.Agents/DependencyInjection.cs
src/AgenticWorkforce.Agents/Runtime/AgentRuntime.cs
src/AgenticWorkforce.Agents/Runtime/IAgentFactory.cs
src/AgenticWorkforce.Agents/Runtime/AgentFactory.cs
src/AgenticWorkforce.Agents/Runtime/IChatClientFactory.cs
src/AgenticWorkforce.Agents/Runtime/ChatClientFactory.cs
src/AgenticWorkforce.Agents/Runtime/StubChatClient.cs
src/AgenticWorkforce.Agents/Prompts/IPromptAssembler.cs
src/AgenticWorkforce.Agents/Prompts/PromptAssembler.cs
src/AgenticWorkforce.Agents/Prompts/Organization/principles.md
src/AgenticWorkforce.Agents/Prompts/Organization/coding-standards.md
src/AgenticWorkforce.Agents/Prompts/Organization/security-posture.md
src/AgenticWorkforce.Agents/Prompts/Organization/communication-style.md
src/AgenticWorkforce.Agents/Prompts/Categories/project.md
src/AgenticWorkforce.Agents/Prompts/Categories/software.md
src/AgenticWorkforce.Agents/Prompts/Categories/research.md
src/AgenticWorkforce.Agents/Prompts/Categories/security.md
src/AgenticWorkforce.Agents/Prompts/Categories/system.md
src/AgenticWorkforce.Agents/Tools/IToolRegistry.cs
src/AgenticWorkforce.Agents/Tools/ToolRegistry.cs
src/AgenticWorkforce.Agents/Tools/ToolBinding.cs
src/AgenticWorkforce.Agents/Tools/FileScopePolicy.cs
src/AgenticWorkforce.Agents/Tools/IFileScopedTool.cs
src/AgenticWorkforce.Agents/Tools/ApprovalRequiredAIFunction.cs
src/AgenticWorkforce.Agents/Tools/ToolRegistration.cs
src/AgenticWorkforce.Agents/Context/IProjectContextProviderFactory.cs
src/AgenticWorkforce.Agents/Context/ProjectContextProviderFactory.cs
src/AgenticWorkforce.Agents/Context/ProjectContextProvider.cs
src/AgenticWorkforce.Agents/Context/IContextAssembler.cs
src/AgenticWorkforce.Agents/Context/ContextAssembler.cs
src/AgenticWorkforce.Agents/Context/ProjectContext.cs
src/AgenticWorkforce.Agents/Context/ContextPacket.cs
src/AgenticWorkforce.Agents/Middleware/BudgetEnforcingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/AuditingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/CostTrackingChatClient.cs
src/AgenticWorkforce.Agents/Middleware/ContentSafetyChatClient.cs
src/AgenticWorkforce.Agents/Catalog/IAgentCatalogResolver.cs
src/AgenticWorkforce.Agents/Catalog/AgentCatalogResolver.cs
src/AgenticWorkforce.Agents/Services/LlmCallDrainService.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Agents/AgentRuntimeTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Agents/PromptAssemblerTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Agents/ToolRegistryTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj — Add MAF packages
src/AgenticWorkforce.Worker/Program.cs — Add AddAgentServices()
Directory.Packages.props — Add MAF package versions
```

### Package Additions

```xml
<PackageVersion Include="Microsoft.Extensions.AI.Agents" Version="9.6.0" />
<PackageVersion Include="Microsoft.Extensions.AI.Agents.Anthropic" Version="9.6.0" />
<PackageVersion Include="Microsoft.Extensions.AI.AzureAIInference" Version="9.6.0" />
<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="9.6.0" />
<PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="10.0.8" />
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test` — unit tests pass:
   - `AgentRuntimeTests`: resolves catalog entry, constructs agent, returns result from stub
   - `PromptAssemblerTests`: assembles 5 layers correctly, handles missing optional layers
   - `ToolRegistryTests`: resolves manifest, throws on unregistered tool, applies file scope
3. `IAgentRuntime` is resolvable from Worker DI container
4. `ChatClientFactory` caches pipelines per provider+model combination
5. `BudgetEnforcingChatClient` throws `BudgetExceededException` when budget is exceeded
6. Embedded resource prompts load correctly (Organization + Categories)
7. No MAF types leak into Domain or Api projects (module boundary rule MB-003/MB-004)
8. `LlmCallDrainService` background service starts and drains channel to repository

---

## Goal Command

```
/goal Agent runtime infrastructure complete in AgenticWorkforce.Agents: AgentRuntime resolves catalog and executes via AgentFactory (6-step construction). ChatClientFactory builds shared IChatClient pipelines with Budget, Audit, CostTracking, FunctionInvocation, ContentSafety, and OTel middleware. PromptAssembler loads embedded org/category prompts and composes 5 layers. ToolRegistry resolves tools from explicit manifest. ContextAssembler builds priority-based context packet. StubChatClient allows testing without real LLM. Worker calls AddAgentServices(). Verify: dotnet build exits 0, dotnet test exits 0 with unit tests for runtime, prompts, and tool registry. Stop after 40 turns.
```
