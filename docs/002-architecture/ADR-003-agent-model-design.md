# ADR-003: Agent Model Design

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R03-response-maf-agent-extensibility.md](../098-research/R03-response-maf-agent-extensibility.md)
**Companion:** [ADR-016 — Agent Design](ADR-016-agent-design.md) (complete catalog, prompts, tools, constraints, lifecycle)

---

## Context

The Agentic Workforce Platform has a rich agent catalog (15+ agents) with versioned prompts, configurable tools, model assignments, constraints, and execution modes. The enterprise TRD adds template inheritance with monotonic guardrails. We need to map this onto MAF's agent abstractions in C#.

## Key MAF Facts (from research)

- `AIAgent` is abstract; `ChatClientAgent` is **sealed** — cannot subclass it
- Extension points: middleware (`AsBuilder().Use()`), `DelegatingAIAgent`, or full `AIAgent` subclass
- Prompts: single `Instructions` string + `AIContextProvider` for per-turn dynamic context
- Sessions: `AgentSession` with typed `ProviderSessionState<T>` via `StateKey`
- Tools: `AIFunctionFactory.Create()` from C# methods; `AsAIFunction()` for agent-as-tool
- Multi-provider: each agent gets its own `IChatClient`, trivially mixable

## Decision

### 1. Agent Catalog: Database-driven factory over `ChatClientAgent`

Agents are defined in the database (`AgentCatalog` entity) and constructed at runtime by an `AgentFactory` service. We do NOT subclass `AIAgent` — instead we compose:

```csharp
public class AgentFactory
{
    public AIAgent Create(AgentCatalogEntry entry, ProjectContext project)
    {
        // 1. Resolve IChatClient for the agent's configured provider/model
        IChatClient client = _providerRegistry.GetClient(entry.ModelConfig);

        // 2. Wrap with middleware pipeline
        client = client.AsBuilder()
            .Use(CostTrackingMiddleware)
            .Use(ContentSafetyMiddleware)
            .Use(AuditLoggingMiddleware)
            .Build();

        // 3. Assemble prompt from 5 layers
        string instructions = _promptAssembler.Assemble(entry, project);

        // 4. Resolve tools from agent's tool list
        IEnumerable<AITool> tools = _toolRegistry.Resolve(entry.Tools, project);

        // 5. Build agent with context provider for per-turn context injection
        var contextProvider = new ProjectContextProvider(project, entry);
        return new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = entry.AgentName,
            Description = entry.Description,
            Instructions = instructions,
            ChatOptions = new ChatOptions { Tools = tools.ToList() },
            AIContextProviderFactory = _ => contextProvider,
            UseProvidedChatClientAsIs = true,  // CRITICAL: we composed our own pipeline
            RequirePerServiceCallChatHistoryPersistence = true, // crash recovery + audit
        });
    }
}
```

### 2. Prompt Assembly: 5-layer composition into single `Instructions` string

MAF's `Instructions` is a single string. We concatenate all five layers:

```csharp
public class PromptAssembler
{
    public string Assemble(AgentCatalogEntry agent, ProjectContext project)
    {
        var sb = new StringBuilder();
        sb.AppendLine(_diskPrompts.LoadOrganization());          // Layer 1: global
        sb.AppendLine(_diskPrompts.LoadCategory(agent.AgentType)); // Layer 2: category
        sb.AppendLine(agent.SystemPrompt);                       // Layer 3: agent (from DB, versioned)
        if (project.Brief is not null)
            sb.AppendLine(project.Brief);                        // Layer 4: project brief
        var userPrompt = _teamRepo.GetUserPrompt(project.Id, agent.Id);
        if (userPrompt is not null)
            sb.AppendLine(userPrompt);                           // Layer 5: user prompt per agent per project
        return sb.ToString();
    }
}
```

### 3. Context Injection: Custom `AIContextProvider` per turn

The context assembly pipeline (PCD, task definition, learnings, code map, history) is injected via a custom `AIContextProvider` that runs before every agent call:

```csharp
internal sealed class ProjectContextProvider : AIContextProvider
{
    public override string StateKey => nameof(ProjectContextProvider);

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken ct)
    {
        var packet = await _assembler.BuildAsync(_project, _task, _agent);
        return new AIContext
        {
            Instructions = packet.ContextText,  // appended to agent instructions
            Messages = packet.HistoryMessages,   // injected into conversation
        };
    }
}
```

### 4. Session State: `ProviderSessionState<T>` for rich session data

```csharp
internal class ProjectSessionState
{
    public string? RollingSummary { get; set; }
    public int RollingSummaryAnchor { get; set; }
    public int RollingSummaryVersion { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public List<ChannelBinding> Channels { get; set; } = [];
    public Guid? ProjectId { get; set; }
}

// In context provider or middleware:
var stateHelper = new ProviderSessionState<ProjectSessionState>(
    _ => new ProjectSessionState(), nameof(ProjectContextProvider));
var state = stateHelper.GetOrInitializeState(session);
state.TotalCostUsd += callCost;
```

Sessions are serialized to DB via `agent.SerializeSession()` for persistence across process restarts.

### 5. Template Inheritance: Domain model pattern (not MAF-specific)

```csharp
public class TemplateResolver
{
    public ResolvedTemplate Resolve(string templateId)
    {
        var chain = LoadInheritanceChain(templateId); // [base, category, concrete]
        var resolved = new ResolvedTemplate();

        foreach (var tmpl in chain)
        {
            resolved.MergeScalars(tmpl);              // child wins
            resolved.MergeTools(tmpl.Tools);           // merge by key, child overrides
            resolved.IntersectDataScopes(tmpl.Scopes); // narrow only
            resolved.AddGuardrails(tmpl.Guardrails);   // additive, never remove
            resolved.StrengthenSupervision(tmpl.Supervision); // more, never fewer
        }

        resolved.ContentHash = ComputeHash(resolved); // pin for deployment
        return resolved;
    }
}
```

The resolved template feeds into `AgentFactory.Create()` — guardrails become constraints checked in middleware; data scopes become `FileScope` rules in tool implementations.

### 6. Agent-as-Tool Composition

The Director agent uses sub-agents as tools:

```csharp
AIAgent planner = _factory.Create(catalog["planner"], project);
AIAgent coder = _factory.Create(catalog["coder"], project);
AIAgent reviewer = _factory.Create(catalog["security.reviewer"], project);

AIAgent director = _factory.Create(catalog["director"], project);
// Override tools to include sub-agents
director = new ChatClientAgent(directorClient, new ChatClientAgentOptions
{
    Instructions = directorInstructions,
    ChatOptions = new ChatOptions
    {
        Tools = [planner.AsAIFunction(), coder.AsAIFunction(), reviewer.AsAIFunction()]
    }
});
```

### 7. Budget Enforcement: Agent middleware

```csharp
async Task<AgentResponse> BudgetMiddleware(
    IEnumerable<ChatMessage> messages, AgentSession? session,
    AgentRunOptions? options, AIAgent inner, CancellationToken ct)
{
    var budget = _budgetService.Check(session, _agentName);
    if (budget.Status == BudgetStatus.Exceeded)
        throw new BudgetExceededException(_agentName, budget.Spent, budget.Ceiling);
    if (budget.Status == BudgetStatus.Warning)
        await _eventBus.PublishAsync(new BudgetWarningEvent(_agentName, budget));

    return await inner.RunAsync(messages, session, options, ct);
}
```

## Alternatives Considered

| Option | Verdict | Why Not |
|--------|---------|---------|
| Subclass `ChatClientAgent` | Not possible | It's `sealed` |
| Custom `AIAgent` subclass for each agent type | Rejected | Over-engineering; our agents differ in configuration, not behaviour |
| `DelegatingAIAgent` wrapper | Available but not needed | Middleware + context providers cover our use cases more cleanly |
| Static agent definitions (code, not DB) | Rejected | Need runtime configurability, versioned prompts, dynamic team composition |

## Consequences

- Agent catalog lives in the database — CRUD via admin API
- System prompts are versioned in `PromptVersion` table (audit trail for prompt changes)
- `AIContextProvider` is limited to one per agent in .NET today (GitHub #2933) — design a composite provider if needed
- Multi-provider agents: each agent gets its own `IChatClient`; no runtime model swapping on the same agent instance — build separate agents and route
- Anthropic provider doesn't support MCP/code interpreter tools — surface these as `AIFunction` wrappers

### Principle Compliance

- **P16 Single Source of Truth:** The `AgentCatalog` table is the single authoritative source for agent definitions. In-memory agent instances from `AgentFactory` are ephemeral derived objects. Disk-based prompts (layers 1-2) are committed to the repository — not editable at runtime. If a conflict arises between cache and DB, the DB wins.
- **P17 Human Authority:** All agent outputs are proposals, not commitments. When an agent's confidence is below threshold or instructions are ambiguous, the agent creates a `human_decision` task and pauses rather than proceeding. Template guardrails and supervision levels can only be overridden by a human with the Owner project role.
- **P18 Idempotency:** Agent creation via `AgentFactory.Create()` is stateless and inherently idempotent. Agent execution side effects (task state changes, PCD mutations, learning extraction) must carry an idempotency key derived from execution ID + step, and check for existing records before writing.

## Key Patterns Summary

| Concern | Pattern | MAF Feature |
|---------|---------|-------------|
| Agent definition | DB catalog → `AgentFactory` → `ChatClientAgent` | `ChatClientAgentOptions` |
| Prompt layering | 5-layer assembly → single `Instructions` string | `Instructions` + `AIContextProvider.Instructions` (appended) |
| Context injection | `ProjectContextProvider : AIContextProvider` | `AIContext { Instructions, Messages, Tools }` per turn |
| Session state | `ProviderSessionState<ProjectSessionState>` | Typed state bag on `AgentSession` |
| Cost tracking | IChatClient middleware | `ChatResponse.Usage` + custom recording |
| Budget enforcement | Agent run middleware | `AsBuilder().Use(BudgetMiddleware)` |
| Template inheritance | Domain model `TemplateResolver` | Feeds resolved config into `AgentFactory` |
| Agent composition | `agent.AsAIFunction()` | Director calls sub-agents as tools |
