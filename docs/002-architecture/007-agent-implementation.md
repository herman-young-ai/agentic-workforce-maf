# Agent Implementation Architecture

**Version:** 1.0
**Date:** 2026-05-12
**Classification:** Internal — Confidential
**Status:** Approved
**Implements:** [ADR-003](ADR-003-agent-model-design.md), [ADR-010](ADR-010-context-assembly.md), [ADR-016](ADR-016-agent-design.md), [ADR-017](ADR-017-project-orchestration.md)
**Research:** [R20-response-agent-implementation-design.md](../098-research/R20-response-agent-implementation-design.md)

---

## 1. Purpose

This document defines the internal architecture of the `AgenticWorkforce.Agents` project — the shared library that both Api and Worker reference. It covers folder structure, naming conventions, tool organisation, orchestration agent design, the seed strategy, and developer experience for adding new agents.

The ADRs define **what** we decided. This document defines **how** it's built.

---

## 2. Project Structure

`AgenticWorkforce.Agents` is the integration layer between MAF and our domain. It owns agent construction, prompt assembly, tool registration, context injection, middleware, and verification.

```
src/AgenticWorkforce.Agents/
├── AgenticWorkforce.Agents.csproj
├── DependencyInjection.cs                  # AddAgentServices() extension method
│
├── Runtime/                                 # MAF wrappers (Principle 4: Wrap the Core)
│   ├── IAgentRuntime.cs                     # Run/stream agents by name
│   ├── AgentRuntime.cs                      # Resolves catalog, builds agent, executes
│   ├── IAgentFactory.cs                     # Create ChatClientAgent from catalog + project
│   ├── AgentFactory.cs                      # 6-step construction (§3.2)
│   ├── IChatClientFactory.cs                # Build/cache IChatClient pipelines
│   └── ChatClientFactory.cs                 # Shared per (provider, model)
│
├── Catalog/                                 # Agent catalog seeding + resolution
│   ├── IAgentCatalogResolver.cs             # Load catalog entry by name + version
│   ├── AgentCatalogResolver.cs              # DB lookup with caching
│   ├── AgentSeedService.cs                  # YAML → DB seeding at startup (§7)
│   └── Seeds/                               # YAML seed files (embedded resources)
│       ├── project.director.yaml
│       ├── project.planner.yaml
│       ├── project.supervisor.yaml
│       ├── research.strategist.yaml
│       ├── research.searcher.yaml
│       ├── research.analyst.yaml
│       ├── research.synthesizer.yaml
│       ├── security.webapp.scanner.yaml
│       ├── security.webapp.triage.yaml
│       ├── security.webapp.reporter.yaml
│       ├── software.code-analyst.yaml
│       ├── software.architecture-reviewer.yaml
│       ├── software.quality-verifier.yaml
│       ├── system.summarizer.yaml
│       ├── system.verifier.yaml
│       └── system.knowledge-officer.yaml
│
├── Prompts/                                 # Prompt assembly + disk-based prompt files
│   ├── IPromptAssembler.cs
│   ├── PromptAssembler.cs                   # 5-layer assembly (§3.4)
│   ├── Organization/                        # Layer 1: global prompts (embedded resources)
│   │   ├── principles.md
│   │   ├── coding-standards.md
│   │   ├── security-posture.md
│   │   └── communication-style.md
│   └── Categories/                          # Layer 2: per-category prompts
│       ├── project.md
│       ├── software.md
│       ├── research.md
│       ├── security.md
│       └── system.md
│
├── Tools/                                   # Tool implementations + registry (§4)
│   ├── IToolRegistry.cs
│   ├── ToolRegistry.cs                      # Central name → AIFunction mapping
│   ├── IFileScopedTool.cs                   # Interface for file-scope enforcement
│   ├── ApprovalRequiredAIFunction.cs        # HITL gate wrapper
│   ├── ToolRegistration.cs                  # DI registration helpers
│   │
│   ├── Common/                              # Cross-cutting tools (used by many agents)
│   │   ├── FileReadTool.cs                  # file.read
│   │   ├── FileWriteTool.cs                 # file.write
│   │   ├── FileSearchTool.cs                # file.search
│   │   ├── ShellExecuteTool.cs              # shell.execute
│   │   ├── WebSearchTool.cs                 # web.search
│   │   └── WebFetchTool.cs                  # web.fetch
│   │
│   ├── Project/                             # Project management tools (Director, Planner)
│   │   ├── GetProjectInfoTool.cs            # project.get_info
│   │   ├── GetProjectTeamTool.cs            # project.get_team
│   │   ├── GetPcdTool.cs                    # project.get_pcd
│   │   ├── GetHistoryTool.cs                # project.get_history
│   │   ├── GetPlanTool.cs                   # project.get_plan
│   │   ├── ListWorkflowsTool.cs             # project.list_workflows
│   │   ├── GetArtifactsTool.cs              # project.get_artifacts
│   │   ├── GetLearningsTool.cs              # project.get_learnings
│   │   ├── RefinePlanTool.cs                # project.refine_plan
│   │   ├── ApproveTasksTool.cs              # project.approve_tasks
│   │   ├── RunObjectiveTool.cs              # project.run_objective
│   │   ├── RunWorkflowTool.cs               # project.run_workflow
│   │   ├── StartResearchTool.cs             # project.start_research
│   │   ├── AddPrincipleTool.cs              # project.add_principle
│   │   └── UpdateBudgetTool.cs              # project.update_budget
│   │
│   ├── Security/                            # Security domain tools
│   │   ├── CodeScanTool.cs                  # security.code.scan
│   │   ├── DependencyScanTool.cs            # security.deps.scan
│   │   ├── SecretScanTool.cs                # security.secrets.scan
│   │   └── VulnLookupTool.cs               # security.vuln.lookup
│   │
│   ├── Research/                            # Research domain tools
│   │   ├── DeepSearchTool.cs                # research.web.search
│   │   ├── ContentExtractTool.cs            # research.extract
│   │   └── SourceEvaluateTool.cs            # research.source.evaluate
│   │
│   ├── Software/                            # Software domain tools
│   │   ├── CodeAnalysisTool.cs              # software.code.analyze
│   │   ├── ArchitectureMapTool.cs           # software.arch.map
│   │   └── TestRunTool.cs                   # software.test.run
│   │
│   ├── Supervisor/                          # Supervisor-specific tools
│   │   ├── GetRecentOutcomesTool.cs          # project.get_recent_outcomes
│   │   └── GetPastDecisionsTool.cs          # project.get_past_decisions
│   │
│   └── Mcp/                                 # MCP tool integration
│       ├── IMcpToolResolver.cs
│       └── McpToolResolver.cs
│
├── Context/                                 # Per-turn context injection (ADR-010)
│   ├── IProjectContextProviderFactory.cs
│   ├── ProjectContextProviderFactory.cs
│   ├── ProjectContextProvider.cs            # Single AIContextProvider per agent
│   ├── IContextAssembler.cs
│   └── ContextAssembler.cs                  # Priority-based token budget allocation
│
├── Middleware/                              # IChatClient pipeline middleware
│   ├── BudgetEnforcingChatClient.cs         # Fail fast on budget exceeded
│   ├── AuditingChatClient.cs                # Write audit records to channel
│   ├── ContentSafetyChatClient.cs           # Content safety checks
│   └── CostTrackingChatClient.cs            # Track token usage + cost per call
│
└── Verification/                            # Post-execution verification (3-tier)
    ├── IVerifier.cs
    ├── VerificationPipeline.cs              # Tier 1 → Tier 2 → Tier 3
    ├── SchemaVerifier.cs                    # Tier 1: JSON schema validation
    ├── RuleVerifier.cs                      # Tier 2: deterministic business rules
    └── AgentVerifier.cs                     # Tier 3: agent-based quality review
```

### Design Rationale

**Tools are organised by domain, not per-agent.** Multiple agents share tools within the same domain (e.g., all security agents share `security.*` tools). Cross-cutting tools live in `Common/`. This prevents duplication and makes tool reuse explicit via the manifest.

**Container-first execution domains (Principle 22).** Every tool is registered with an `ExecutionDomain`:

| Domain | Folder | Tools | Runs In |
|--------|--------|-------|---------|
| **Platform** (in-process) | `Tools/Project/`, `Tools/Supervisor/` | `project.*` (get_info, get_pcd, get_team, approve_tasks, etc.) | Worker process — internal DB queries only |
| **Sandbox** (containerized) | `Tools/Common/`, `Tools/Security/`, `Tools/Research/`, `Tools/Software/` | `file.*`, `shell.*`, `web.*`, `git.*`, `security.*`, `research.*`, `software.*` | ACA Dynamic Sessions (Hyper-V) or MCP container |

New tools default to Sandbox. Registering as Platform requires explicit justification. Architecture tests verify no Platform tool makes outbound HTTP calls.

**Prompts are embedded resources.** Layers 1-2 (organisation and category prompts) are `.md` files compiled into the assembly. This eliminates filesystem dependencies in containerised deployments. Layer 3 (agent system prompt) is in the DB, seeded from YAML. Layers 4-5 are runtime data.

**No special folders for Director/Supervisor/Planner.** They are catalog entries like any other agent. Their specialness comes from seed YAML, prompt files, and tool assignments — not from code structure. Their tools live in `Tools/Project/` and `Tools/Supervisor/`.

**Adding a new category** (e.g., "finance") requires: one new prompt file in `Prompts/Categories/`, one new tool folder in `Tools/Finance/`, and seed YAMLs in `Catalog/Seeds/`. No restructuring.

---

## 3. Base Agent Infrastructure

### 3.1 IAgentRuntime — Wrap the Core

The runtime wrapper provides a stable interface over MAF. Consumers (Api, Worker, Dispatch Engine) never reference MAF types directly.

```csharp
public interface IAgentRuntime
{
    Task<AgentResult> RunAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default);

    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default);
}

public record AgentRunOptions(
    string? PromptVariant = null,                  // "chat" for chat mode
    Dictionary<string, object>? Inputs = null,     // upstream task inputs
    TimeSpan? Timeout = null                       // override default timeout
);

public record AgentResult(
    string Output, decimal CostUsd, TimeSpan Duration,
    int ToolCallCount, TokenUsage Tokens, string AgentVersion);
```

Implementation resolves the catalog entry, builds the agent via `IAgentFactory`, enforces the timeout, and maps MAF's response into our `AgentResult`:

```csharp
internal sealed class AgentRuntime(
    IAgentCatalogResolver catalog,
    IAgentFactory factory,
    IProjectAgentRepository projectAgents,
    TimeProvider clock) : IAgentRuntime
{
    public async Task<AgentResult> RunAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default)
    {
        var entry = await catalog.ResolveAsync(agentName, ct);
        var projectAgent = await projectAgents.GetAsync(context.ProjectId, entry.Id, ct);

        var timeout = options?.Timeout ?? TimeSpan.FromSeconds(entry.TimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var agent = factory.Create(entry, context, projectAgent, options?.PromptVariant);
        var session = await agent.CreateSessionAsync(cts.Token);
        var start = clock.GetTimestamp();

        var response = await agent.RunAsync(
            FormatObjective(objective, options?.Inputs), session, ct: cts.Token);

        return new AgentResult(
            Output: response.Text,
            CostUsd: ExtractCost(response),
            Duration: clock.GetElapsedTime(start),
            ToolCallCount: CountToolCalls(response),
            Tokens: ExtractTokens(response),
            AgentVersion: entry.Version);
    }
}
```

### 3.2 AgentFactory — 6-Step Construction

The factory implements the construction flow defined in ADR-016 §5. Every agent is a `ChatClientAgent` — no subclasses.

```csharp
internal sealed class AgentFactory(
    IChatClientFactory clientFactory,
    IPromptAssembler promptAssembler,
    IToolRegistry toolRegistry,
    IMcpToolResolver mcpResolver,
    IProjectContextProviderFactory contextProviderFactory,
    IServiceProvider sp) : IAgentFactory
{
    public AIAgent Create(
        AgentCatalog catalog, ProjectContext project,
        ProjectAgent? projectAgent = null, string? promptVariant = null)
    {
        // Step 1: Shared IChatClient pipeline (cached per provider+model)
        var client = clientFactory.GetOrCreate(catalog.ModelProvider, catalog.ModelName);

        // Step 2: Assemble 5-layer prompt
        var instructions = promptAssembler.Assemble(
            catalog, project.Brief, projectAgent?.UserPrompt, promptVariant);

        // Step 3: Resolve tools from manifest + apply project overrides
        var fileScope = JsonSerializer.Deserialize<FileScopePolicy>(catalog.FileScope)!;
        var manifest = JsonSerializer.Deserialize<List<ToolBinding>>(catalog.ToolManifest)!;
        MergeProjectOverrides(manifest, projectAgent);
        var tools = toolRegistry.Resolve(manifest, sp, fileScope);

        // Step 4: Resolve MCP tools (bypasses ToolRegistry — resolved from MCP servers)
        foreach (var binding in manifest.Where(b => b.McpServer is not null))
            tools.Add(mcpResolver.Resolve(binding));

        // Step 5: Create context provider for per-turn injection
        var contextProvider = contextProviderFactory.Create(project, catalog);

        // Step 6: Build ChatClientAgent
        return new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = catalog.AgentName,
            Description = catalog.Description,
            Instructions = instructions,
            ChatOptions = new ChatOptions
            {
                Tools = tools,
                Temperature = catalog.Temperature,
                MaxOutputTokens = catalog.MaxOutputTokens,
            },
            UseProvidedChatClientAsIs = true,
            RequirePerServiceCallChatHistoryPersistence = true,
            AIContextProviderFactory = _ => contextProvider,
        });
    }

    private static void MergeProjectOverrides(List<ToolBinding> manifest, ProjectAgent? pa)
    {
        if (pa?.AdditionalTools is not null)
            manifest.AddRange(
                JsonSerializer.Deserialize<List<ToolBinding>>(pa.AdditionalTools)!);
        if (pa?.RestrictedTools is not null)
        {
            var restricted =
                JsonSerializer.Deserialize<HashSet<string>>(pa.RestrictedTools)!;
            manifest.RemoveAll(t => restricted.Contains(t.Name));
        }
    }
}
```

### 3.3 ChatClientFactory — Shared IChatClient Pipelines

One pipeline per `(provider, model)`. Agent construction reuses the cached pipeline — only `ChatClientAgentOptions` varies per agent.

```csharp
internal sealed class ChatClientFactory(
    IServiceProvider sp) : IChatClientFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();

    public IChatClient GetOrCreate(string provider, string model)
        => _clients.GetOrAdd($"{provider}:{model}", _ => BuildPipeline(provider, model));

    private IChatClient BuildPipeline(string provider, string model)
    {
        IChatClient raw = provider switch
        {
            "foundry-anthropic" => sp.GetRequiredService<FoundryAnthropicClient>()
                .AsIChatClient(model),
            "azure-openai" => sp.GetRequiredService<AzureOpenAIClient>()
                .GetChatClient(model).AsIChatClient(),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };

        return raw.AsBuilder()
            .Use((inner, svc) => new BudgetEnforcingChatClient(inner,
                svc.GetRequiredService<IBudgetService>()))
            .Use((inner, svc) => new AuditingChatClient(inner,
                svc.GetRequiredService<ChannelWriter<AuditRecord>>()))
            .UseFunctionInvocation()
            .Use((inner, svc) => new ContentSafetyChatClient(inner,
                svc.GetRequiredService<IContentSafetyService>()))
            .UseOpenTelemetry(sourceName: "AgenticWorkforce.Agents")
            .Build(sp);
    }
}
```

**Pipeline order matters.** Budget is outermost (checked first, cheapest to reject). OTel is innermost (wraps the raw provider call). FunctionInvocation sits in the middle so tool calls are audited and budget-checked.

### 3.4 PromptAssembler — 5-Layer Composition

```csharp
internal sealed class PromptAssembler : IPromptAssembler
{
    private readonly IReadOnlyDictionary<string, string> _orgPrompts;
    private readonly IReadOnlyDictionary<string, string> _categoryPrompts;

    public PromptAssembler()
    {
        _orgPrompts = LoadEmbeddedMarkdown("Prompts.Organization");
        _categoryPrompts = LoadEmbeddedMarkdown("Prompts.Categories");
    }

    public string Assemble(
        AgentCatalog agent, string? projectBrief,
        string? userPrompt, string? promptVariant = null)
    {
        var parts = new List<string>();

        // Layer 1: Organization prompts (all files, sorted by name)
        parts.AddRange(_orgPrompts.OrderBy(kv => kv.Key).Select(kv => kv.Value));

        // Layer 2: Category prompt
        if (_categoryPrompts.TryGetValue(agent.Category, out var catPrompt))
            parts.Add(catPrompt);

        // Layer 3: Agent system prompt (or variant for chat mode)
        parts.Add(promptVariant ?? agent.SystemPrompt);

        // Layer 4: Project brief
        if (!string.IsNullOrEmpty(projectBrief))
            parts.Add($"## Project Brief\n\n{projectBrief}");

        // Layer 5: User prompt (additive, per agent per project)
        if (!string.IsNullOrEmpty(userPrompt))
            parts.Add($"## Project-Specific Instructions\n\n{userPrompt}");

        return string.Join("\n\n---\n\n", parts);
    }
}
```

**Layers 1-3 are static** — set at agent construction via `Instructions`.
**Layers 4-5 are semi-static** — set once per project context, not per turn.
**Per-turn dynamic context** (PCD, learnings, task definition, code map, history) comes through `AIContextProvider` as defined in ADR-010. This is context, not instructions.

### 3.5 ProjectContextProvider

The single `AIContextProvider` per agent (MAF 1.5.0 limitation). Composes all per-turn context concerns into one provider.

```csharp
internal sealed class ProjectContextProvider(
    IContextAssembler assembler,
    ProjectContext project,
    AgentCatalog agent) : AIContextProvider
{
    public override string StateKey => nameof(ProjectContextProvider);

    protected override async ValueTask<AIContext> InvokingAsync(
        InvokingContext ctx, CancellationToken ct)
    {
        var packet = await assembler.BuildAsync(
            project.ProjectId, project.TaskDefinition,
            agent, project.DomainTags, ct);

        return new AIContext
        {
            Messages = packet.ContextMessages,
            Instructions = packet.AdditionalInstructions,
            Tools = packet.DynamicTools,
        };
    }

    protected override async ValueTask InvokedAsync(
        InvokedContext ctx, CancellationToken ct)
    {
        // Rolling summary compression when session exceeds token budget
        var state = new ProviderSessionState<ProjectSessionState>(
            _ => new(), StateKey).GetOrInitializeState(ctx.Session);
        state.TurnCount++;
    }
}
```

---

## 4. Tool Organisation

### 4.1 ToolRegistry

Central registry mapping tool names (strings) to `AIFunction` factories. Explicit registration only — no auto-discovery (Principle 21).

```csharp
public interface IToolRegistry
{
    void Register(string name, Func<IServiceProvider, AITool> factory);
    IList<AITool> Resolve(
        IEnumerable<ToolBinding> manifest, IServiceProvider sp,
        FileScopePolicy fileScope);
}

internal sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, AITool>> _factories = new();

    public void Register(string name, Func<IServiceProvider, AITool> factory)
        => _factories[name] = factory;

    public IList<AITool> Resolve(
        IEnumerable<ToolBinding> manifest, IServiceProvider sp,
        FileScopePolicy fileScope)
    {
        var tools = new List<AITool>();
        foreach (var binding in manifest.Where(b => b.McpServer is null))
        {
            if (!_factories.TryGetValue(binding.Name, out var factory))
                throw new InvalidOperationException(
                    $"Tool '{binding.Name}' not registered.");

            var tool = factory(sp);

            if (tool is IFileScopedTool scoped)
                scoped.SetScope(fileScope);

            if (binding.RequiresApproval)
                tool = new ApprovalRequiredAIFunction((AIFunction)tool);

            tools.Add(tool);
        }
        return tools;
    }
}
```

### 4.2 Tool Categories

| Category | Registry Prefix | Folder | Description |
|----------|----------------|--------|-------------|
| **Cross-cutting** | `file.*`, `shell.*`, `web.*` | `Tools/Common/` | Used by many agents. Sandboxed file ops, shell, web search/fetch. |
| **Project** | `project.*` | `Tools/Project/` | Director and Planner tools. Query state, trigger work, manage settings. |
| **Security** | `security.*` | `Tools/Security/` | Code scanning, dependency scanning, secret scanning, vulnerability lookup. |
| **Research** | `research.*` | `Tools/Research/` | Deep web search, content extraction, source evaluation. |
| **Software** | `software.*` | `Tools/Software/` | Code analysis, architecture mapping, test execution. |
| **Supervisor** | `project.get_recent_outcomes`, `project.get_past_decisions` | `Tools/Supervisor/` | Supervisor-specific read tools. |
| **MCP** | Varies | `Tools/Mcp/` | External MCP server tools. Resolved at construction time, not pre-registered. |

### 4.3 Tool Registration in DI

```csharp
public static class ToolRegistration
{
    public static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry>(sp =>
        {
            var registry = new ToolRegistry();

            // Common
            registry.Register("file.read", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<FileReadTool>().ReadFileAsync));
            registry.Register("file.write", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<FileWriteTool>().WriteFileAsync));
            registry.Register("file.search", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<FileSearchTool>().SearchAsync));
            registry.Register("shell.execute", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<ShellExecuteTool>().ExecuteAsync));
            registry.Register("web.search", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<WebSearchTool>().SearchAsync));
            registry.Register("web.fetch", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<WebFetchTool>().FetchAsync));

            // Project
            registry.Register("project.get_info", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetProjectInfoTool>().GetInfoAsync));
            registry.Register("project.get_plan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetPlanTool>().GetPlanAsync));
            registry.Register("project.refine_plan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<RefinePlanTool>().RefinePlanAsync));
            registry.Register("project.run_objective", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<RunObjectiveTool>().RunObjectiveAsync));
            // ... remaining project tools follow the same pattern

            // Security
            registry.Register("security.code.scan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<CodeScanTool>().ScanAsync));
            registry.Register("security.deps.scan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<DependencyScanTool>().ScanAsync));
            registry.Register("security.secrets.scan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<SecretScanTool>().ScanAsync));
            registry.Register("security.vuln.lookup", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<VulnLookupTool>().LookupAsync));

            // Research
            registry.Register("research.web.search", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<DeepSearchTool>().SearchAsync));
            registry.Register("research.extract", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<ContentExtractTool>().ExtractAsync));
            registry.Register("research.source.evaluate", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<SourceEvaluateTool>().EvaluateAsync));

            // Software
            registry.Register("software.code.analyze", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<CodeAnalysisTool>().AnalyzeAsync));
            registry.Register("software.arch.map", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<ArchitectureMapTool>().MapAsync));
            registry.Register("software.test.run", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<TestRunTool>().RunAsync));

            // Supervisor
            registry.Register("project.get_recent_outcomes", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetRecentOutcomesTool>().GetOutcomesAsync));
            registry.Register("project.get_past_decisions", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetPastDecisionsTool>().GetDecisionsAsync));

            return registry;
        });

        // Register tool implementation classes
        services.AddScoped<FileReadTool>();
        services.AddScoped<FileWriteTool>();
        services.AddScoped<FileSearchTool>();
        services.AddScoped<ShellExecuteTool>();
        services.AddScoped<WebSearchTool>();
        services.AddScoped<WebFetchTool>();
        services.AddScoped<CodeScanTool>();
        services.AddScoped<DependencyScanTool>();
        services.AddScoped<SecretScanTool>();
        services.AddScoped<VulnLookupTool>();
        services.AddScoped<DeepSearchTool>();
        services.AddScoped<ContentExtractTool>();
        services.AddScoped<SourceEvaluateTool>();
        services.AddScoped<CodeAnalysisTool>();
        services.AddScoped<ArchitectureMapTool>();
        services.AddScoped<TestRunTool>();
        services.AddScoped<GetRecentOutcomesTool>();
        services.AddScoped<GetPastDecisionsTool>();
        // ... project tools
        return services;
    }
}
```

### 4.4 Tool Implementation Pattern

Every tool follows the same structure. File-accessing tools implement `IFileScopedTool` for scope enforcement at the tool level (not middleware).

```csharp
public sealed class FileReadTool : IFileScopedTool
{
    private readonly IDynamicSessionClient _session;
    private FileScopePolicy _scope = new([], []);

    public void SetScope(FileScopePolicy scope) => _scope = scope;

    [Description("Read a file from the project workspace")]
    public async Task<string> ReadFileAsync(
        [Description("Relative path within the workspace")] string path,
        CancellationToken ct = default)
    {
        Guard.Against.PathTraversal(path);
        Guard.Against.OutsideScope(path, _scope.ReadPaths);
        return await _session.ExecuteAsync($"cat '{path}'", ct);
    }
}
```

### 4.5 MCP Tool Resolution

MCP tools are resolved from external MCP servers at agent construction time. They bypass `ToolRegistry` — the manifest declares which server provides them.

```csharp
internal sealed class McpToolResolver(IMcpClientFactory mcpFactory) : IMcpToolResolver
{
    public AITool Resolve(ToolBinding binding)
    {
        var client = mcpFactory.GetOrCreate(binding.McpServer!);
        var tools = client.ListToolsAsync().GetAwaiter().GetResult();
        return tools.FirstOrDefault(t => t.Name == binding.Name)
            ?? throw new InvalidOperationException(
                $"MCP tool '{binding.Name}' not found on server '{binding.McpServer}'");
    }
}
```

---

## 5. Naming Conventions

### 5.1 Convention Table

| Artifact | Convention | Examples |
|----------|-----------|----------|
| Agent name (catalog) | `{category}.{subcategory?}.{name}` | `project.director`, `security.webapp.scanner`, `research.strategist` |
| Agent C# class | None — all config-driven | No agent classes. `ChatClientAgent` is the only runtime type. |
| Tool name (registry) | `{domain}.{subdomain?}.{action}` | `file.read`, `security.code.scan`, `project.get_plan` |
| Tool C# class | `{Action}Tool.cs` | `FileReadTool`, `CodeScanTool`, `GetPlanTool` |
| Tool C# method | `{Action}Async` | `ReadFileAsync`, `ScanAsync`, `GetPlanAsync` |
| Org prompt file | `{topic}.md` | `principles.md`, `security-posture.md` |
| Category prompt | `{category}.md` | `project.md`, `security.md` |
| Seed YAML | `{agent-name}.yaml` | `project.director.yaml`, `security.webapp.scanner.yaml` |
| Category | Lowercase singular noun | `project`, `software`, `research`, `security`, `system` |
| Interface | `I{Concept}` | `IAgentRuntime`, `IToolRegistry`, `IPromptAssembler` |
| Implementation | `{Concept}` | `AgentRuntime`, `ToolRegistry`, `PromptAssembler` |

### 5.2 Subcategory Rules

Use subcategories when a category has **distinct operational domains** that warrant different prompt context and tool sets:

- `security.webapp` — web application security (OWASP, DAST, code scanning)
- `security.cloud` — cloud infrastructure security (IAM, network, compliance)
- `security.data` — data security (encryption, masking, access control)

Do NOT use subcategories for role differentiation within the same domain. `research.searcher` is correct — not `research.web.searcher`.

---

## 6. Orchestration Agents

Three agents are built into the platform. They're catalog entries like any other agent — but they have platform-level tools, auto-assignment rules, and specific interactions with the Dispatch Engine.

### 6.1 Project Director

**Purpose:** Human's conversational delegate. Manager, not worker. Delegates everything.

**Seed YAML** (`project.director.yaml`):

```yaml
agent_name: project.director
agent_type: horizontal
category: project
version: "1.0.0"
description: >-
  Conversational project delegate. Answers questions, triggers work,
  manages the plan, reports results. Manager, not worker.
enabled: true
chat_enabled: true
invocation_tier: platform

model:
  provider: foundry-anthropic
  name: claude-sonnet-4-6
  temperature: 0.0
  max_output_tokens: 16384

tools:
  # Read tools (query state)
  - { name: project.get_info }
  - { name: project.get_team }
  - { name: project.get_pcd }
  - { name: project.get_history }
  - { name: project.get_plan }
  - { name: project.list_workflows }
  - { name: project.get_artifacts }
  - { name: project.get_learnings }
  # Plan tools
  - { name: project.refine_plan }
  - { name: project.approve_tasks }
  # Action tools
  - { name: project.run_objective }
  - { name: project.run_workflow }
  - { name: project.start_research }
  # Settings tools
  - { name: project.add_principle }
  - { name: project.update_budget }

constraints:
  max_budget_usd: 5.00
  max_tool_calls: 20
  timeout_seconds: 300

file_scope:
  read_paths: []
  write_paths: []
```

**System prompt key sections** (Layer 3 — full prompt defined in ADR-017 §Role 1):
- You are the user's delegate — manage the project on their behalf
- Delegate, don't DIY — use tools for all specialist work
- Confirm before costly actions — state budget impact before triggering workflows
- Use tools for data — never guess or fabricate information
- You do NOT create task plans (the Planner does that via `refine_plan`)
- You do NOT call specialist agents directly (the Dispatch Engine does that)

**Auto-assignment:** `AgentSeedService` assigns `project.director` as a `ProjectAgent` with `Role = "director"` to every project on creation. Cannot be removed.

**Dispatch interaction:** Director calls `project.run_objective` → creates a Task with `Source = TaskSource.Director` → triggers the Dispatch Engine Durable Task orchestrator. Director calls `project.refine_plan` → invokes the Planner via `IAgentRuntime.RunAsync("project.planner", ...)`.

### 6.2 Planner

**Purpose:** Creates and refines task plans. Produces structured task DAGs with agent assignments, dependencies, and verification criteria.

**Seed YAML** (`project.planner.yaml`):

```yaml
agent_name: project.planner
agent_type: horizontal
category: project
version: "1.0.0"
description: >-
  Creates and refines task plans. Receives objectives, produces task DAGs
  with agent assignments, dependencies, and verification criteria.
enabled: true
chat_enabled: false
invocation_tier: system

model:
  provider: foundry-anthropic
  name: claude-sonnet-4-6
  temperature: 0.0
  max_output_tokens: 8192
  max_thinking_tokens: 4096

tools:
  - { name: project.get_info }
  - { name: project.get_team }
  - { name: project.get_plan }
  - { name: project.get_learnings }

constraints:
  max_budget_usd: 2.00
  max_tool_calls: 10
  timeout_seconds: 120

interface_contract:
  output:
    tasks: "array<TaskProposal>"
  response_schema: TaskPlanSchema
  schema_name: TaskPlan
```

**System prompt key sections:**
- You create task plans — you receive objectives and produce structured task DAGs
- You see the team roster (available agents, tools, constraints, input/output contracts)
- Each task specifies: agent name, objective, dependencies, verification criteria, expected inputs/outputs
- You do NOT execute or approve tasks — you propose them
- Use extended thinking for complex multi-step plans

**Auto-assignment:** NOT auto-assigned. Invoked by the Director via `project.refine_plan`, or by the Supervisor via the Dispatch Engine's `RefinePlan` activity.

**Dispatch interaction:** Writes Task rows to the DB with `Status = Proposed`. The Director or human then approves them, and the Dispatch Engine picks up approved tasks.

### 6.3 Supervisor

**Purpose:** Post-run decision maker. Classifies outcomes and routes the next action. Haiku-class (~$0.003 per decision).

**Seed YAML** (`project.supervisor.yaml`):

```yaml
agent_name: project.supervisor
agent_type: horizontal
category: project
version: "1.0.0"
description: >-
  Post-run decision maker. Classifies outcomes and routes next action.
  Haiku-class — classifies, does not reason deeply.
enabled: true
chat_enabled: true
invocation_tier: platform

model:
  provider: foundry-anthropic
  name: claude-haiku-4-5
  temperature: 0.0
  max_output_tokens: 1000

tools:
  - { name: project.get_plan }
  - { name: project.get_recent_outcomes }
  - { name: project.get_past_decisions }

constraints:
  max_budget_usd: 0.50
  max_tool_calls: 5
  timeout_seconds: 30

interface_contract:
  output:
    decision: "enum(wait|advance|refine|complete|escalate)"
    reasoning: "string"
    tasks_to_advance: "array<string>?"
    refinement_request: "string?"
  response_schema: SupervisorOutputSchema
  schema_name: SupervisorOutput
```

**System prompt key sections** (full prompt defined in ADR-017 §Role 3):
- You classify and route — you do NOT plan or execute
- Five decisions: `wait`, `advance`, `refine`, `complete`, `escalate`
- Always check the plan first, then recent outcomes
- Never advance more than 2 iterations without human check-in → `escalate`

**Auto-assignment:** Auto-assigned to every project alongside Director. Cannot be removed.

**Dispatch interaction:** Called as a Durable Task activity after each dispatch cycle completes. Its `SupervisorOutput` (structured output) drives deterministic routing:

```
SupervisorDecision.Wait     → orchestration completes
SupervisorDecision.Advance  → approve tasks, re-dispatch
SupervisorDecision.Refine   → invoke Planner, re-dispatch
SupervisorDecision.Complete → propose completion to human
SupervisorDecision.Escalate → create human_decision task, wait
```

---

## 7. Agent Seed Strategy

### 7.1 Seed Flow

```
YAML files (embedded resources in Agents project)
    ↓
AgentSeedService.StartAsync() — runs on application startup
    ↓
Parse YAML → AgentCatalog entity
    ↓
Check: does (AgentName, Version) already exist in DB?
    YES → skip (idempotent)
    NO  → insert new row
    ↓
Auto-assign platform agents to all existing projects
    ↓
DB AgentCatalog table is the single source of truth at runtime
```

### 7.2 AgentSeedService

```csharp
internal sealed class AgentSeedService(
    IAgentCatalogRepository repo,
    IProjectRepository projects,
    ILogger<AgentSeedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var seeds = LoadEmbeddedYaml("Catalog.Seeds");

        foreach (var seed in seeds)
        {
            var existing = await repo.FindAsync(seed.AgentName, seed.Version, ct);
            if (existing is not null)
            {
                log.LogDebug("Agent {Name} v{Version} already seeded",
                    seed.AgentName, seed.Version);
                continue;
            }

            var entry = MapToEntity(seed);
            await repo.InsertAsync(entry, ct);
            log.LogInformation("Seeded agent {Name} v{Version}",
                seed.AgentName, seed.Version);

            // Auto-assign platform agents to all existing projects
            if (entry.InvocationTier == "platform")
                await AutoAssignToAllProjects(entry, ct);
        }
    }

    private async Task AutoAssignToAllProjects(AgentCatalog entry, CancellationToken ct)
    {
        var projectIds = await projects.GetAllIdsAsync(ct);
        foreach (var projectId in projectIds)
        {
            var exists = await repo.ProjectAgentExistsAsync(projectId, entry.Id, ct);
            if (!exists)
            {
                await repo.AssignToProjectAsync(projectId, entry.Id,
                    role: InferRole(entry), ct);
            }
        }
    }
}
```

### 7.3 Version Upgrade Strategy

| Scenario | Action | Effect |
|----------|--------|--------|
| New prompt for existing agent | Bump version in YAML (`1.0.0` → `1.1.0`) | New row inserted. Existing projects continue on old version until explicitly upgraded. |
| New tool added to manifest | Bump version | New row with updated manifest. |
| Model change | Bump version | New row. Projects must opt-in to the upgrade. |
| Deprecation | Set `deprecated_at` + `deprecation_message` | Seed service updates existing row. UI shows warning. No new assignments. |
| Retirement | Set `enabled: false` on deprecated entry | Cannot be assigned to new projects. Existing projects keep cached definition. |

### 7.4 Invocation Tiers

| Tier | Meaning | Examples | Auto-assigned? |
|------|---------|----------|---------------|
| `platform` | Always present in every project. Cannot be removed. | Director, Supervisor | Yes |
| `system` | Invoked by other agents or the Dispatch Engine. Not directly user-facing. | Planner, Summarizer, Verifier | No |
| `user` | Specialist agents. Assigned to projects by owners or templates. | Security Scanner, Code Analyst, Research Strategist | No |

---

## 8. Adding a New Agent — Developer Guide

### Step 1: Create the Seed YAML

Create `src/AgenticWorkforce.Agents/Catalog/Seeds/{category}.{name}.yaml`:

```yaml
agent_name: security.cloud.scanner
agent_type: vertical
category: security
version: "1.0.0"
description: >-
  Scans cloud infrastructure for security misconfigurations,
  IAM policy violations, and compliance gaps.
enabled: false          # P14: disabled by default
chat_enabled: false
invocation_tier: user

model:
  provider: foundry-anthropic
  name: claude-sonnet-4-6
  temperature: 0.0
  max_output_tokens: 8192

tools:
  - { name: file.read }
  - { name: file.search }
  - { name: security.cloud.scan }

constraints:
  max_budget_usd: 3.00
  max_tool_calls: 30
  timeout_seconds: 600

file_scope:
  read_paths: ["@workspace/infra/", "@workspace/bicep/", "@workspace/terraform/"]
  write_paths: ["@workspace/evidence/"]

interface_contract:
  input:
    scope: "string"
    compliance_framework: "string"
  output:
    findings: "array<Finding>"
    summary: "object"
  response_schema: CloudScanResultSchema
  schema_name: CloudScanResult
```

### Step 2: Write the Category Prompt (if new category)

Not needed if the category already exists. If adding a new category (e.g., `finance`), create `src/AgenticWorkforce.Agents/Prompts/Categories/finance.md`.

### Step 3: Implement New Tools (if any)

Create `src/AgenticWorkforce.Agents/Tools/Security/CloudScanTool.cs`:

```csharp
public sealed class CloudScanTool : IFileScopedTool
{
    private FileScopePolicy _scope = new([], []);
    public void SetScope(FileScopePolicy scope) => _scope = scope;

    [Description("Scan cloud infrastructure files for security misconfigurations")]
    public async Task<string> ScanAsync(
        [Description("Directory containing IaC files")] string directory,
        [Description("Compliance framework: cis|nist|pci")] string framework,
        CancellationToken ct = default)
    {
        Guard.Against.PathTraversal(directory);
        Guard.Against.OutsideScope(directory, _scope.ReadPaths);
        // Implementation: parse IaC files, apply rules, return findings
    }
}
```

### Step 4: Register the Tool

In `ToolRegistration.cs`, add:

```csharp
registry.Register("security.cloud.scan", sp => AIFunctionFactory.Create(
    sp.GetRequiredService<CloudScanTool>().ScanAsync));
services.AddScoped<CloudScanTool>();
```

### Step 5: Embedded Resources (already handled)

The `.csproj` uses wildcards — new YAML and `.md` files are automatically included:

```xml
<ItemGroup>
  <EmbeddedResource Include="Catalog/Seeds/*.yaml" />
  <EmbeddedResource Include="Prompts/**/*.md" />
</ItemGroup>
```

### Step 6: Test Locally

```bash
# Start the Aspire AppHost
dotnet run --project src/AgenticWorkforce.AppHost

# Agent is seeded on startup with enabled: false
# Test via admin API
curl -X POST localhost:5000/api/v1/admin/catalog/security.cloud.scanner/test \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"input": "Scan /infra for CIS violations"}'
```

### Step 7: Enable and Assign

```bash
# Enable the agent
curl -X PATCH localhost:5000/api/v1/admin/catalog/security.cloud.scanner \
  -d '{"enabled": true}'
```

The agent now appears in the catalog browser. Project owners can assign it to their projects, or it can be included in project templates.

### Checklist

| # | Step | File(s) |
|---|------|---------|
| 1 | Create seed YAML | `Catalog/Seeds/{name}.yaml` |
| 2 | Write category prompt (if new category) | `Prompts/Categories/{category}.md` |
| 3 | Implement tools (if new) | `Tools/{Domain}/{Tool}.cs` |
| 4 | Register tools in DI | `ToolRegistration.cs` |
| 5 | Deploy — seed service auto-inserts | — |
| 6 | Test with `enabled: false` via admin API | — |
| 7 | Enable via admin API | — |
| 8 | Assign to projects (or via template) | — |

---

## 9. Future-Proofing

### New Category (e.g., "finance")

1. Add `Prompts/Categories/finance.md`
2. Add `Tools/Finance/` with domain tools
3. Register tools in `ToolRegistration.cs`
4. Add seed YAMLs in `Catalog/Seeds/`
5. No structural changes. Categories are string fields validated against available prompt files.

### New Subcategory (e.g., "security.cloud")

1. Add seed YAMLs with dotted names: `security.cloud.scanner.yaml`
2. Add tools in `Tools/Security/` (or `Tools/Security/Cloud/` if many)
3. The category prompt (`security.md`) covers all subcategories
4. No code changes — subcategories are naming conventions, not structural

### Custom Agent Runtime Type

For agents that need something other than `ChatClientAgent` (e.g., `DelegatingAIAgent`, custom `AIAgent` subclass), add a `RuntimeType` field to `AgentCatalog`:

```csharp
// In AgentFactory.Create():
return catalog.RuntimeType switch
{
    "chat_client" or null => BuildChatClientAgent(catalog, project, ...),
    "delegating" => BuildDelegatingAgent(catalog, project, ...),
    _ => throw new ArgumentException($"Unknown runtime type: {catalog.RuntimeType}")
};
```

New runtime types require code changes — by design (Principle 21).

### A2A (Remote) Agents

Represented in the catalog with `runtime_type: "a2a"`:

```yaml
agent_name: external.compliance-checker
runtime_type: a2a
a2a_endpoint: https://compliance-agent.internal.investec.com/.well-known/agent.json
```

The `AgentFactory` creates MAF's built-in `A2AAgent` instead of `ChatClientAgent`. Tools, prompts, and constraints are still recorded in the catalog for visibility, but execution is delegated to the remote agent.

### Custom IChatClient Pipeline

Add a `pipeline_override` field to the catalog. `ChatClientFactory` caches overridden pipelines separately:

```csharp
public IChatClient GetOrCreate(
    string provider, string model, string? pipelineOverride = null)
{
    var key = pipelineOverride is not null
        ? $"{provider}:{model}:{pipelineOverride}"
        : $"{provider}:{model}";
    return _clients.GetOrAdd(key,
        _ => BuildPipeline(provider, model, pipelineOverride));
}
```

Custom pipelines are rare. Use cases: skip content safety for internal-only agents, add domain-specific middleware.

### Scale: 50+ Agents

The structure holds at scale. Tools are domain-organised (not per-agent), so 50 agents sharing ~25 tool classes across 6 domain folders is manageable. Seed YAMLs are flat files. `ToolRegistry` is a dictionary lookup — O(1) per tool. `ChatClientFactory` caches pipelines — agent construction is cheap (options + shared client).

### Multi-Tenant

Add an optional `TenantId` column to `AgentCatalog` (`null` = platform-wide). `AgentCatalogResolver` filters by tenant. Prompt Layers 1-2 can be tenant-overridable via a `Prompts/Tenants/{tenantId}/` overlay. Not needed for Phase 1 — Investec is a single tenant.

---

## 10. Differences from Mission Control Prototype

| Aspect | Prototype (Python) | Production (C#) | Rationale |
|--------|-------------------|-----------------|-----------|
| Agent definitions | YAML files on disk | YAML seeds → DB catalog | DB enables versioning, per-project overrides, runtime queries |
| Agent construction | Direct class instantiation | `AgentFactory` + sealed `ChatClientAgent` | MAF constraint; composition over inheritance |
| Prompt files | `config/prompts/` filesystem | Embedded resources in Agents project | No filesystem dependency in containers |
| Agent system prompt | Disk file (Layer 3) | DB field on `AgentCatalog` (seeded from YAML) | Enables versioning, A/B testing, runtime editing |
| Tool registration | Decorator-based auto-discovery | Explicit `ToolRegistry.Register()` | P21: Explicit Over Implicit |
| Agent naming | `{category}.{subcategory?}.{name}.agent` | `{category}.{subcategory?}.{name}` | Dropped `.agent` suffix — redundant in typed catalog |
| Categories | Filesystem folders | String field + prompt file convention | Same categories, different enforcement |
| Dispatch loop | Python async loop | Durable Task orchestrator | Crash recovery, pod restart durability |
| Supervisor | Session followup primitive | Explicit Durable Task activity | Auditable, versioned, structured output |
| Platform agents | `platform/` category | `invocation_tier: platform` on any category | Decouples "always-on" from category |
| Orchestration agents | Custom class hierarchy | Same `ChatClientAgent` as all agents | Specialness comes from config, not code |

### What We Kept

- **Dotted agent naming** (`security.webapp.scanner`) — proven intuitive
- **5-layer prompt assembly** — organisation, category, agent, project, user
- **Tool manifest pattern** — explicit allowlist per agent
- **Director-Planner-Supervisor triad** — validated in prototype
- **Gate modes** — off, interactive, ai_assisted, autonomous
- **Category prompt files** — per-domain system context

### What We Changed

- **DB-driven catalog** replaces filesystem YAML — enables versioning, API, lifecycle
- **Embedded resources** replace filesystem prompt files — container-friendly
- **Sealed `ChatClientAgent`** replaces class hierarchy — MAF constraint turned into a feature (all agents are config, not code)
- **Durable Task** replaces async dispatch loop — crash recovery in a regulated bank
- **Explicit tool registration** replaces auto-discovery — audit trail, no surprises

---

## 11. Error Handling

| Scenario | Behaviour | Principle |
|----------|-----------|-----------|
| Agent name not found in catalog | `AgentNotFoundException` — fail fast, do not fallback to a default agent | P8, P14 |
| Tool in manifest not registered in `ToolRegistry` | `InvalidOperationException("Tool 'x' not registered")` — fail at agent construction, not at execution | P8 |
| MCP server unreachable | `McpConnectionException` — fail fast, do not skip the tool silently | P8 |
| Category prompt file missing | **Silent skip** — Layer 2 is optional. Log a warning. Agent still functions with Layers 1, 3-5. | Design choice: categories enhance, don't gate |
| IChatClient pipeline build failure (bad provider) | `ArgumentException` — fail at startup when `ChatClientFactory` first builds the pipeline | P8 |
| Foundry/provider unavailable at runtime | Propagates provider exception through the IChatClient pipeline. `BudgetEnforcingChatClient` does NOT catch provider errors — they surface to the caller. | P8 |
| Agent execution exceeds timeout | `OperationCanceledException` via `CancellationTokenSource.CancelAfter(timeout)` in `AgentRuntime` | P19 |
| Tool call exceeds tool call limit | `MaxToolCallsExceededException` — tracked in `FunctionInvokingChatClient.MaximumIterationsPerRequest` | P19 |
| Prompt assembly produces empty string | `InvalidOperationException("Empty prompt")` — at least Layer 3 (system prompt) must be present | P8 |

**Rule:** Fail fast and loud (Principle 8). Never silently degrade agent capabilities. A misconfigured agent that runs with wrong tools is more dangerous than one that refuses to start.

---

## 12. Testing Strategy

### Unit Tests

| What | How | Mocks |
|------|-----|-------|
| Individual tool logic | Test tool C# methods directly | Mock `IDynamicSessionClient` for sandboxed tools, mock `HttpClient` for web tools |
| `PromptAssembler` | Assert 5-layer composition with known inputs | No mocks needed — pure string assembly |
| `ToolRegistry.Resolve()` | Assert correct tools returned for a manifest | Register test factories |
| `AgentFactory.Create()` | Assert `ChatClientAgentOptions` has correct tools, instructions, settings | Mock `IChatClientFactory`, `IToolRegistry`, `IPromptAssembler` |
| File scope enforcement | Assert `Guard.Against.OutsideScope` throws for invalid paths | No mocks — pure validation |
| `MergeProjectOverrides` | Assert additional/restricted tools merge correctly | No mocks — pure logic |

### Integration Tests

| What | How |
|------|-----|
| Agent construction end-to-end | Seed a test catalog entry, build via `AgentFactory`, verify MAF agent has expected tools and options |
| Tool registration completeness | Assert every tool name in every seed YAML is registered in `ToolRegistry` — catches manifest/registry drift |
| Prompt file completeness | Assert every category referenced in seed YAMLs has a corresponding prompt file in embedded resources |
| Seed idempotency | Run `AgentSeedService` twice — assert no duplicate rows, no errors |

### Prompt Regression Tests

| What | How |
|------|-----|
| Agent output quality | Record LLM interactions from audit pipeline (ADR-008). Replay with deterministic inputs. Assert output structure matches expected schema. |
| Prompt drift detection | Hash the assembled 5-layer prompt for each agent. Compare against a baseline hash. Flag when prompts change unexpectedly. |
| Tool call patterns | For a known input, assert the agent calls the expected tools in the expected order. Uses recorded audit data. |

### Local Testing Without LLM

```csharp
// Mock IChatClient that returns canned responses
var mockClient = new MockChatClient(responses: new Dictionary<string, string>
{
    ["Scan /src/auth for vulnerabilities"] = """{"findings": [{"id": 1, "severity": "high"}]}"""
});

var factory = new AgentFactory(
    new ChatClientFactory(mockClient),  // inject mock
    promptAssembler, toolRegistry, mcpResolver, contextProviderFactory, sp);

var agent = factory.Create(catalogEntry, projectContext);
var result = await agent.RunAsync("Scan /src/auth for vulnerabilities");
Assert.Contains("findings", result.Text);
```

---

## 13. Observability

### Agent Construction Logging

```csharp
// In AgentFactory.Create():
_logger.LogInformation(
    "Agent constructed: {AgentName} v{Version} with {ToolCount} tools, provider={Provider}, model={Model}, variant={Variant}",
    catalog.AgentName, catalog.Version, tools.Count,
    catalog.ModelProvider, catalog.ModelName, promptVariant ?? "system");
```

### Agent Execution Metrics

The `AgentRuntime` emits custom metrics alongside MAF's built-in OpenTelemetry:

```csharp
private static readonly Meter _meter = new("AgenticWorkforce.Agents");
private static readonly Counter<long> _invocations = _meter.CreateCounter<long>("agent.invocations");
private static readonly Histogram<double> _duration = _meter.CreateHistogram<double>("agent.duration_ms");
private static readonly Counter<decimal> _cost = _meter.CreateCounter<decimal>("agent.cost_usd");

// In RunAsync():
_invocations.Add(1,
    new("agent", agentName), new("project", context.ProjectId.ToString()));
_duration.Record(result.Duration.TotalMilliseconds,
    new("agent", agentName));
_cost.Add(result.CostUsd,
    new("agent", agentName), new("model", entry.ModelName));
```

### What Gets Traced (OpenTelemetry)

| Span | Source | Attributes |
|------|--------|-----------|
| `agent.run {agentName}` | `AgentRuntime` | agent, project_id, version, variant, timeout |
| `agent.construct {agentName}` | `AgentFactory` | agent, provider, model, tool_count |
| `chat {model}` | MAF `UseOpenTelemetry` | model, input_tokens, output_tokens |
| `tool.invoke {toolName}` | MAF `FunctionInvokingChatClient` | tool, duration |

### Alert Conditions

| Alert | Condition | Severity |
|-------|-----------|----------|
| Agent construction failure | Any `AgentNotFoundException` or `InvalidOperationException` in `AgentFactory` | High |
| Agent timeout | `OperationCanceledException` in `AgentRuntime` | Medium |
| Tool not found | `InvalidOperationException("Tool 'x' not registered")` | Critical (deployment issue) |
| Seed failure | `AgentSeedService` logs error during startup | Critical |

---

## 14. Concurrency and Thread Safety

| Component | Thread Safety | Notes |
|-----------|--------------|-------|
| `ChatClientFactory._clients` | `ConcurrentDictionary` — safe for concurrent `GetOrAdd` | Pipeline built once, reused across all agents sharing that provider+model |
| `ToolRegistry._factories` | **Read-only after startup.** All `Register()` calls happen during DI configuration (single-threaded). After that, only `Resolve()` reads. | If dynamic tool registration is ever needed, replace with `ConcurrentDictionary` |
| `PromptAssembler._orgPrompts`, `_categoryPrompts` | Immutable after construction. Loaded once from embedded resources. | Safe for concurrent reads |
| `AgentFactory.Create()` | Stateless — safe to call concurrently | Each call produces an independent `ChatClientAgent` instance |
| `MergeProjectOverrides()` | **Must clone the manifest before modifying.** The current code modifies the deserialized list in place — this is safe because `JsonSerializer.Deserialize` creates a new list per call. If the manifest were ever cached, this would be a bug. | Document this invariant: manifest is always freshly deserialized per `Create()` call |
| `AgentRuntime.RunAsync()` | Stateless — safe. `ProjectContext` is passed per call, not shared. | `AsyncLocal` is NOT used (see ADR-009 correction — explicit parameter passing) |
| `ProjectContextProvider` | Single instance per agent, but `ProviderSessionState<T>` stores per-session state on the `AgentSession` — not on the provider. | Safe for concurrent sessions with different `AgentSession` instances |

**Rule:** Agent instances (`ChatClientAgent`) are cheap to create and are NOT cached. Each `AgentRuntime.RunAsync()` call constructs a fresh agent. This eliminates stale config risk — if the catalog changes, the next call picks it up. The `IChatClient` pipeline IS cached (expensive to build), but it's stateless and thread-safe by contract.

---

## 15. Prompt Variant Handling

Two prompt modes per chat-enabled agent:

| Mode | Variant | Layer 3 Source | When Used |
|------|---------|---------------|-----------|
| **Execution** (default) | `null` | `AgentCatalog.SystemPrompt` | Agent runs a task via Dispatch Engine |
| **Chat** | `"chat"` | `AgentCatalog.ChatPrompt` (nullable field) | Human chats with the agent directly |

If `ChatPrompt` is null and variant `"chat"` is requested, fall back to `SystemPrompt` (the agent behaves the same in both modes — some agents don't need a chat variant).

```csharp
// In PromptAssembler.Assemble():
// Layer 3: Agent system prompt (or chat variant)
var layer3 = promptVariant == "chat" && !string.IsNullOrEmpty(agent.ChatPrompt)
    ? agent.ChatPrompt
    : agent.SystemPrompt;
parts.Add(layer3);
```

**Seed YAML supports both:**

```yaml
agent_name: project.director
system_prompt: |
  You are the Project Director...
  [full execution-mode prompt]
chat_prompt: |
  You are the Project Director in chat mode...
  [conversational variant — more concise, different behavioral guidelines]
```

Both are versioned in the `PromptVersion` table. Chat prompt changes = version bump, same as system prompt.

---

## 16. DependencyInjection Entry Point

The single extension method that Api and Worker both call:

```csharp
// DependencyInjection.cs
public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services, IConfiguration config)
    {
        // Runtime wrappers
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();

        // Catalog
        services.AddSingleton<IAgentCatalogResolver, AgentCatalogResolver>();
        services.AddHostedService<AgentSeedService>();

        // Prompts
        services.AddSingleton<IPromptAssembler, PromptAssembler>();

        // Tools
        services.AddAgentTools();  // from ToolRegistration.cs

        // MCP
        services.AddSingleton<IMcpToolResolver, McpToolResolver>();

        // Context
        services.AddSingleton<IProjectContextProviderFactory, ProjectContextProviderFactory>();
        services.AddSingleton<IContextAssembler, ContextAssembler>();

        // Middleware components (injected into ChatClientFactory pipeline)
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<IContentSafetyService, ContentSafetyService>();

        // Verification
        services.AddSingleton<IVerifier, VerificationPipeline>();

        return services;
    }
}

// In Api/Program.cs and Worker/Program.cs:
builder.Services.AddAgentServices(builder.Configuration);
```

---

## 17. Seed YAML Schema Validation

Seed YAMLs are validated against a strongly-typed model at deserialization time. Invalid YAMLs fail the seed service startup (Principle 8: Fail Fast).

```csharp
public sealed class AgentSeedDefinition
{
    [Required] public string AgentName { get; set; } = null!;
    [Required] public string AgentType { get; set; } = null!;   // validated: horizontal | vertical
    [Required] public string Category { get; set; } = null!;    // validated against known categories
    [Required] public string Version { get; set; } = null!;     // validated: semver format
    [Required] public string Description { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool ChatEnabled { get; set; }
    public string InvocationTier { get; set; } = "user";        // validated: user | system | platform

    [Required] public ModelConfig Model { get; set; } = null!;
    public List<ToolBindingSeed> Tools { get; set; } = [];
    public ConstraintsSeed? Constraints { get; set; }
    public FileScopeSeed? FileScope { get; set; }
    public InterfaceContractSeed? InterfaceContract { get; set; }

    public string? SystemPrompt { get; set; }                   // inline or loaded from embedded .md
    public string? ChatPrompt { get; set; }
}

// In AgentSeedService.StartAsync():
foreach (var seed in seeds)
{
    var errors = Validator.TryValidateObject(seed, new ValidationContext(seed), results, validateAllProperties: true);
    if (!errors)
        throw new InvalidOperationException(
            $"Invalid seed YAML for '{seed.AgentName}': {string.Join(", ", results.Select(r => r.ErrorMessage))}");

    if (!KnownCategories.Contains(seed.Category))
        throw new InvalidOperationException(
            $"Unknown category '{seed.Category}' in seed '{seed.AgentName}'. Known: {string.Join(", ", KnownCategories)}");
}
```

**Known categories validated at seed time:**

```csharp
private static readonly HashSet<string> KnownCategories =
    ["project", "software", "research", "security", "system"];
```

Adding a new category requires adding it to this set AND creating the category prompt file. If the set is updated but the prompt file is missing, a warning is logged (Layer 2 is optional — see §11 Error Handling).

---

## 18. Principle Compliance

- **P4 Wrap the Core:** `IAgentRuntime` and `IAgentFactory` wrap MAF. Api and Worker never import `Microsoft.Agents.AI` directly — they reference our interfaces.
- **P8 Fail Fast:** Invalid agent names, missing tools, bad providers all throw at construction or startup — never at execution time when it's too late.
- **P14 Secure by Default:** New agents seeded with `enabled: false`. Empty tool manifest = zero tools. File scope defaults to empty (no access). Tools must be explicitly registered.
- **P15 Backend Owns All Logic:** All agent construction, prompt assembly, tool resolution, and execution are server-side. The UI displays catalog info — it doesn't construct agents.
- **P16 Single Source of Truth:** `AgentCatalog` table is the authoritative source. Seed YAMLs are the authoring format; DB is the runtime truth. Embedded prompt files are compiled into the assembly — not editable at runtime.
- **P17 Human Authority:** Humans control agent lifecycle (enable/disable/deprecate). User prompts (Layer 5) are human-authored. Platform agents (Director, Supervisor) cannot be removed from projects.
- **P18 Idempotency:** `AgentSeedService` is idempotent — re-seeding the same (name, version) is a no-op. `AgentFactory.Create()` is stateless — same inputs produce same agent. `ChatClientFactory.GetOrAdd` is idempotent.
- **P19 Bounded Resource Usage:** Every agent has `MaxBudgetUsd`, `MaxToolCalls`, `TimeoutSeconds`. Agent construction has no unbounded operations. `FunctionInvokingChatClient.MaximumIterationsPerRequest` caps tool loops.
- **P20 Version Everything:** Agent definitions versioned by `(AgentName, Version)`. Prompts versioned in `PromptVersion` table. Tools are registered at startup with the assembly version.
- **P21 Explicit Over Implicit:** Tool registration is explicit (`ToolRegistry.Register()`). No auto-discovery. Agent names, categories, and tool names follow explicit conventions. Categories validated against a known set.
