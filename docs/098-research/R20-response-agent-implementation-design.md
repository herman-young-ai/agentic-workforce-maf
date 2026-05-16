# R20 Response: Agent Implementation Design

**Research prompt:** R20-prompt-agent-implementation-design.md
**Date:** 2026-05-12
**Constraints:** ADR-003, ADR-010, ADR-016, ADR-017

---

## SECTION 1: Solution Structure — `AgenticWorkforce.Agents`

```
src/AgenticWorkforce.Agents/
├── AgenticWorkforce.Agents.csproj
├── DependencyInjection.cs                  # AddAgentServices() extension method
│
├── Runtime/                                 # MAF wrappers (Principle 4: Wrap the Core)
│   ├── IAgentRuntime.cs                     # Run/stream agents by name
│   ├── AgentRuntime.cs                      # Implementation — resolves catalog, builds, runs
│   ├── IAgentFactory.cs                     # Create ChatClientAgent from catalog + project
│   ├── AgentFactory.cs                      # 5-step construction (ADR-016 §5)
│   ├── IChatClientFactory.cs                # Build/cache IChatClient pipelines
│   └── ChatClientFactory.cs                 # Shared per (provider, model)
│
├── Catalog/                                 # Agent catalog seeding + resolution
│   ├── IAgentCatalogResolver.cs             # Load catalog entry by name + version
│   ├── AgentCatalogResolver.cs              # DB lookup with caching
│   ├── AgentSeedService.cs                  # YAML → DB seeding at startup
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
│   ├── PromptAssembler.cs                   # 5-layer assembly (ADR-016 §3)
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
├── Tools/                                   # Tool implementations + registry
│   ├── IToolRegistry.cs
│   ├── ToolRegistry.cs                      # Central name → AIFunction mapping
│   ├── IFileScopedTool.cs                   # Interface for file-scope enforcement
│   ├── ApprovalRequiredAIFunction.cs        # HITL gate wrapper
│   ├── ToolRegistration.cs                  # DI registration helpers
│   │
│   ├── Common/                              # Cross-cutting tools (used by many agents)
│   │   ├── FileReadTool.cs                  # file.read — sandboxed
│   │   ├── FileWriteTool.cs                 # file.write — sandboxed
│   │   ├── FileSearchTool.cs                # file.search — sandboxed
│   │   ├── ShellExecuteTool.cs              # shell.execute — sandboxed
│   │   ├── WebSearchTool.cs                 # web.search — failover chain
│   │   └── WebFetchTool.cs                  # web.fetch — URL extraction
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
│       ├── IMcpToolResolver.cs              # Resolve MCP tools at construction time
│       └── McpToolResolver.cs               # Connects to MCP servers, resolves by name
│
├── Context/                                 # Per-turn context injection (ADR-010)
│   ├── IProjectContextProviderFactory.cs    # Create provider per agent invocation
│   ├── ProjectContextProviderFactory.cs
│   ├── ProjectContextProvider.cs            # Single AIContextProvider (PCD, task, learnings, history)
│   ├── IContextAssembler.cs                 # Build context packet with token budgets
│   └── ContextAssembler.cs                  # Priority-based token allocation
│
├── Middleware/                              # IChatClient pipeline middleware
│   ├── BudgetEnforcingChatClient.cs         # Fail fast on budget exceeded
│   ├── AuditingChatClient.cs                # Write audit records to channel
│   ├── ContentSafetyChatClient.cs           # Content safety checks
│   └── CostTrackingChatClient.cs            # Track token usage + cost per call
│
└── Verification/                            # Post-execution verification (3-tier)
    ├── IVerifier.cs
    ├── VerificationPipeline.cs              # Tier 1 (schema) → Tier 2 (rules) → Tier 3 (agent)
    ├── SchemaVerifier.cs                    # Tier 1: JSON schema validation
    ├── RuleVerifier.cs                      # Tier 2: deterministic business rules
    └── AgentVerifier.cs                     # Tier 3: agent-based quality review
```

### Design Rationale

**Tools are organised by domain, not per-agent.** Multiple agents share tools within the same domain (e.g., all security agents share `security.*` tools). Cross-cutting tools live in `Common/`. This prevents duplication and makes tool reuse explicit.

**Prompts live alongside the assembly code** as embedded resources. Layers 1-2 are disk files deployed with the application. Layer 3 is in the DB (seeded from YAML). Layers 4-5 are runtime data in the DB. This matches ADR-016's design: static layers are deployed, dynamic layers are persisted.

**No special folders for Director/Supervisor/Planner.** They're catalog entries like any other agent — their specialness comes from their seed YAML, prompt files, and tool assignments, not from code structure. Their tools live in `Tools/Project/` and `Tools/Supervisor/`.

**Adding a new category** (e.g., "finance") requires: one new prompt file in `Prompts/Categories/`, one new tool folder in `Tools/Finance/`, and seed YAMLs in `Catalog/Seeds/`. No restructuring needed.

---

## SECTION 2: Naming Conventions

| Artifact | Convention | Examples |
|----------|-----------|----------|
| **Agent name (catalog)** | `{category}.{subcategory?}.{name}` | `project.director`, `security.webapp.scanner`, `research.strategist`, `project.supervisor` |
| **Agent C# class** | None — everything is config-driven | No agent classes. `ChatClientAgent` is the only runtime type. |
| **Tool name (registry)** | `{domain}.{subdomain?}.{action}` | `file.read`, `security.code.scan`, `project.get_plan`, `web.search` |
| **Tool C# class** | `{Action}Tool.cs` in domain folder | `FileReadTool`, `CodeScanTool`, `GetPlanTool` |
| **Tool C# method** | `{Action}Async` (the `[Description]` method) | `ReadFileAsync`, `ScanCodeAsync`, `GetPlanAsync` |
| **Org prompt file** | `{topic}.md` in `Prompts/Organization/` | `principles.md`, `security-posture.md` |
| **Category prompt file** | `{category}.md` in `Prompts/Categories/` | `project.md`, `security.md`, `research.md` |
| **Seed YAML file** | `{agent-name}.yaml` in `Catalog/Seeds/` | `project.director.yaml`, `security.webapp.scanner.yaml` |
| **Category name** | lowercase singular noun | `project`, `software`, `research`, `security`, `system` |
| **Interface** | `I{Concept}` — no `Service` suffix | `IAgentRuntime`, `IToolRegistry`, `IPromptAssembler` |
| **Implementation** | `{Concept}` matching interface | `AgentRuntime`, `ToolRegistry`, `PromptAssembler` |

### Subcategory Rules

Use subcategories when a category has **distinct operational domains** that warrant different prompt context and tool sets:

- `security.webapp` — web application security (OWASP, DAST, code scanning)
- `security.cloud` — cloud infrastructure security (IAM, network, compliance)
- `security.data` — data security (encryption, masking, access control)

Do NOT use subcategories for:
- Agent role differentiation within the same domain (`research.searcher`, not `research.web.searcher`)
- Deployment-specific concerns (use model config, not naming)

---

## SECTION 3: Base Agent Infrastructure

### a) IAgentRuntime

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
    string? PromptVariant = null,       // "chat" for chat mode
    Dictionary<string, object>? Inputs = null,  // upstream task inputs
    TimeSpan? Timeout = null            // override default timeout
);

public record AgentResult(
    string Output, decimal CostUsd, TimeSpan Duration,
    int ToolCallCount, TokenUsage Tokens, string AgentVersion);
```

### b) AgentRuntime Implementation

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

### c) AgentFactory

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
        // 1. Shared IChatClient pipeline
        var client = clientFactory.GetOrCreate(catalog.ModelProvider, catalog.ModelName);

        // 2. Assemble 5-layer prompt
        var instructions = promptAssembler.Assemble(
            catalog, project.Brief, projectAgent?.UserPrompt, promptVariant);

        // 3. Resolve tools from manifest
        var fileScope = JsonSerializer.Deserialize<FileScopePolicy>(catalog.FileScope)!;
        var manifest = JsonSerializer.Deserialize<List<ToolBinding>>(catalog.ToolManifest)!;
        MergeProjectOverrides(manifest, projectAgent);
        var tools = toolRegistry.Resolve(manifest, sp, fileScope);

        // 4. Resolve MCP tools
        foreach (var binding in manifest.Where(b => b.McpServer is not null))
            tools.Add(mcpResolver.Resolve(binding));

        // 5. Create context provider
        var contextProvider = contextProviderFactory.Create(project, catalog);

        // 6. Build agent
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
            manifest.AddRange(JsonSerializer.Deserialize<List<ToolBinding>>(pa.AdditionalTools)!);
        if (pa?.RestrictedTools is not null)
        {
            var restricted = JsonSerializer.Deserialize<HashSet<string>>(pa.RestrictedTools)!;
            manifest.RemoveAll(t => restricted.Contains(t.Name));
        }
    }
}
```

### d) ToolRegistry

```csharp
public interface IToolRegistry
{
    void Register(string name, Func<IServiceProvider, AITool> factory);
    IList<AITool> Resolve(IEnumerable<ToolBinding> manifest, IServiceProvider sp, FileScopePolicy fileScope);
}

internal sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, AITool>> _factories = new();

    public void Register(string name, Func<IServiceProvider, AITool> factory)
        => _factories[name] = factory;

    public IList<AITool> Resolve(
        IEnumerable<ToolBinding> manifest, IServiceProvider sp, FileScopePolicy fileScope)
    {
        var tools = new List<AITool>();
        foreach (var binding in manifest.Where(b => b.McpServer is null))
        {
            if (!_factories.TryGetValue(binding.Name, out var factory))
                throw new InvalidOperationException($"Tool '{binding.Name}' not registered.");

            var tool = factory(sp);
            if (tool is IFileScopedTool scoped) scoped.SetScope(fileScope);
            if (binding.RequiresApproval) tool = new ApprovalRequiredAIFunction((AIFunction)tool);
            tools.Add(tool);
        }
        return tools;
    }
}
```

### e) PromptAssembler

```csharp
internal sealed class PromptAssembler : IPromptAssembler
{
    private readonly IReadOnlyDictionary<string, string> _orgPrompts;
    private readonly IReadOnlyDictionary<string, string> _categoryPrompts;

    public PromptAssembler(IHostEnvironment env)
    {
        _orgPrompts = LoadEmbeddedMarkdown("Prompts.Organization");
        _categoryPrompts = LoadEmbeddedMarkdown("Prompts.Categories");
    }

    public string Assemble(
        AgentCatalog agent, string? projectBrief,
        string? userPrompt, string? promptVariant = null)
    {
        var parts = new List<string>();

        // Layer 1: Organization (all files, sorted by name)
        parts.AddRange(_orgPrompts.OrderBy(kv => kv.Key).Select(kv => kv.Value));

        // Layer 2: Category
        if (_categoryPrompts.TryGetValue(agent.Category, out var catPrompt))
            parts.Add(catPrompt);

        // Layer 3: Agent system prompt (or variant)
        parts.Add(promptVariant ?? agent.SystemPrompt);

        // Layer 4: Project brief
        if (!string.IsNullOrEmpty(projectBrief))
            parts.Add($"## Project Brief\n\n{projectBrief}");

        // Layer 5: User prompt
        if (!string.IsNullOrEmpty(userPrompt))
            parts.Add($"## Project-Specific Instructions\n\n{userPrompt}");

        return string.Join("\n\n---\n\n", parts);
    }
}
```

### f) ProjectContextProvider

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
        // Compression logic delegated to ContextAssembler
    }
}
```

### g) ChatClientFactory

```csharp
internal sealed class ChatClientFactory(
    IServiceProvider sp, IConfiguration config) : IChatClientFactory
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

---

## SECTION 4: Tool Organisation

### Tool Registration in DI

```csharp
public static class ToolRegistration
{
    public static IServiceCollection AddAgentTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry>(sp =>
        {
            var registry = new ToolRegistry();

            // Common tools
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

            // Project tools (Director, Planner)
            registry.Register("project.get_info", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetProjectInfoTool>().GetInfoAsync));
            registry.Register("project.get_plan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetPlanTool>().GetPlanAsync));
            registry.Register("project.refine_plan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<RefinePlanTool>().RefinePlanAsync));
            registry.Register("project.run_objective", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<RunObjectiveTool>().RunObjectiveAsync));
            // ... remaining project tools follow the same pattern

            // Security tools
            registry.Register("security.code.scan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<CodeScanTool>().ScanAsync));
            registry.Register("security.deps.scan", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<DependencyScanTool>().ScanAsync));

            // Research tools
            registry.Register("research.web.search", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<DeepSearchTool>().SearchAsync));
            registry.Register("research.extract", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<ContentExtractTool>().ExtractAsync));

            // Supervisor tools
            registry.Register("project.get_recent_outcomes", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetRecentOutcomesTool>().GetOutcomesAsync));
            registry.Register("project.get_past_decisions", sp => AIFunctionFactory.Create(
                sp.GetRequiredService<GetPastDecisionsTool>().GetDecisionsAsync));

            return registry;
        });

        // Register tool implementations
        services.AddScoped<FileReadTool>();
        services.AddScoped<FileWriteTool>();
        services.AddScoped<ShellExecuteTool>();
        // ... etc

        return services;
    }
}
```

### MCP Tool Resolution

```csharp
internal sealed class McpToolResolver(IMcpClientFactory mcpFactory) : IMcpToolResolver
{
    public AITool Resolve(ToolBinding binding)
    {
        if (binding.McpServer is null)
            throw new ArgumentException("ToolBinding has no McpServer");

        var client = mcpFactory.GetOrCreate(binding.McpServer);
        var tools = client.ListToolsAsync().GetAwaiter().GetResult();
        return tools.FirstOrDefault(t => t.Name == binding.Name)
            ?? throw new InvalidOperationException(
                $"MCP tool '{binding.Name}' not found on server '{binding.McpServer}'");
    }
}
```

MCP tools are registered in the agent's tool manifest with `McpServer` set. They bypass `ToolRegistry` — resolved directly from the MCP server at agent construction time. This keeps MCP integration explicit (Principle 21) while avoiding registration of remote tools in the local registry.

### Tool Implementation Pattern

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

---

## SECTION 5: The Three Orchestration Agents

### Director

**Seed YAML** (`Catalog/Seeds/project.director.yaml`):

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
  - { name: project.get_info }
  - { name: project.get_team }
  - { name: project.get_pcd }
  - { name: project.get_history }
  - { name: project.get_plan }
  - { name: project.list_workflows }
  - { name: project.get_artifacts }
  - { name: project.get_learnings }
  - { name: project.refine_plan }
  - { name: project.approve_tasks }
  - { name: project.run_objective }
  - { name: project.run_workflow }
  - { name: project.start_research }
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

**System prompt** (Layer 3 — key sections): See ADR-017 §Role 1. Director is a manager — delegates via tools, never does specialist work. Uses `refine_plan` to delegate to Planner, `run_objective` to trigger Dispatch Engine.

**Auto-assignment:** When a project is created, `AgentSeedService` ensures `project.director` and `project.supervisor` are auto-assigned as `ProjectAgent` entries with `Role = "director"` and `Role = "supervisor"`.

**Dispatch interaction:** Director calls `project.run_objective` tool → creates a Task with `Source = TaskSource.Director` → triggers the Dispatch Engine Durable Task orchestrator.

### Planner

**Seed YAML** (`Catalog/Seeds/project.planner.yaml`):

```yaml
agent_name: project.planner
agent_type: horizontal
category: project
version: "1.0.0"
description: >-
  Creates and refines task plans. Receives objectives, produces
  task DAGs with agent assignments, dependencies, and verification criteria.
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
- Role: You create task plans. You receive objectives and produce structured task DAGs.
- You see the team roster (available agents, their tools, constraints, interfaces).
- Each task must specify: agent name, objective, dependencies, verification criteria, expected inputs/outputs.
- You do NOT execute tasks. You do NOT approve tasks. You propose them.
- Use extended thinking for complex multi-step plans.

**Auto-assignment:** Planner is NOT auto-assigned to projects. It's invoked by the Director via `project.refine_plan` (which calls `IAgentRuntime.RunAsync("project.planner", ...)`).

**Dispatch interaction:** Planner writes Task rows to the DB with `Status = Proposed`. The Director or human then approves them, and the Dispatch Engine picks up approved tasks.

### Supervisor

**Seed YAML** (`Catalog/Seeds/project.supervisor.yaml`):

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

**System prompt:** See ADR-017 §Role 3. Supervisor classifies and routes — it does not plan or execute. Structured output with constrained enum.

**Auto-assignment:** Auto-assigned to every project alongside Director.

**Dispatch interaction:** Called as a Durable Task activity after each dispatch cycle completes. Its structured output (`SupervisorDecision`) drives the deterministic routing in the orchestrator.

---

## SECTION 6: Adding a New Agent — Developer Experience

### Step-by-step: Adding `security.cloud.scanner`

**Step 1: Create the seed YAML**

```
src/AgenticWorkforce.Agents/Catalog/Seeds/security.cloud.scanner.yaml
```

```yaml
agent_name: security.cloud.scanner
agent_type: vertical
category: security
version: "1.0.0"
description: >-
  Scans cloud infrastructure configurations for security misconfigurations,
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
  - { name: security.cloud.scan }     # new tool — implement in Step 3

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

**Step 2: Write the category prompt (if new category)**

Not needed — `security` category already exists at `Prompts/Categories/security.md`.

If adding a new category (e.g., `finance`), create `Prompts/Categories/finance.md`.

**Step 3: Implement new tools (if any)**

```
src/AgenticWorkforce.Agents/Tools/Security/CloudScanTool.cs
```

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
        // Implementation...
    }
}
```

**Step 4: Register the tool**

In `ToolRegistration.cs`:

```csharp
registry.Register("security.cloud.scan", sp => AIFunctionFactory.Create(
    sp.GetRequiredService<CloudScanTool>().ScanAsync));
services.AddScoped<CloudScanTool>();
```

**Step 5: Mark the seed YAML as an embedded resource**

In `AgenticWorkforce.Agents.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Catalog/Seeds/*.yaml" />
  <EmbeddedResource Include="Prompts/**/*.md" />
</ItemGroup>
```

(Already a wildcard — new files are automatically included.)

**Step 6: Test locally**

```bash
# Run the Aspire AppHost
dotnet run --project src/AgenticWorkforce.AppHost

# Seed the catalog (runs automatically on startup via AgentSeedService)
# The new agent appears in the catalog but with enabled: false

# Test via admin API
curl -X POST localhost:5000/api/v1/admin/catalog/security.cloud.scanner/test \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"input": "Scan /infra for CIS violations"}'
```

**Step 7: Enable and assign to projects**

```bash
# Enable the agent
curl -X PATCH localhost:5000/api/v1/admin/catalog/security.cloud.scanner \
  -d '{"enabled": true}'

# Now it appears in the agent catalog browser
# Project owners can assign it to their projects via the UI
```

### Checklist Summary

1. Create `Catalog/Seeds/{name}.yaml` — agent definition
2. Create `Prompts/Categories/{category}.md` — only if new category
3. Create `Tools/{Domain}/{Tool}.cs` — only if new tools needed
4. Register new tools in `ToolRegistration.cs`
5. Test via admin API with `enabled: false`
6. Enable via admin API
7. Assign to projects via UI or template

---

## SECTION 7: Agent Seed Strategy

### Seed Flow

```
YAML files (embedded resources)
    ↓ AgentSeedService.SeedAsync() — runs on startup
    ↓ Parse YAML → AgentCatalog entity
    ↓ Check: does this (AgentName, Version) already exist?
    ↓   YES → skip (idempotent)
    ↓   NO  → insert new row
    ↓ Auto-assign platform agents to all projects
DB AgentCatalog table
```

### Implementation

```csharp
internal sealed class AgentSeedService(
    IAgentCatalogRepository repo, ILogger<AgentSeedService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var seeds = LoadEmbeddedYaml();
        foreach (var seed in seeds)
        {
            var existing = await repo.FindAsync(seed.AgentName, seed.Version, ct);
            if (existing is not null)
            {
                log.LogDebug("Agent {Name} v{Version} already seeded, skipping",
                    seed.AgentName, seed.Version);
                continue;
            }

            var entry = MapToEntity(seed);
            await repo.InsertAsync(entry, ct);
            log.LogInformation("Seeded agent {Name} v{Version}", seed.AgentName, seed.Version);

            // Auto-assign platform agents to all existing projects
            if (entry.InvocationTier == "platform")
                await AutoAssignToAllProjects(entry, ct);
        }
    }
}
```

### Version Upgrade Strategy

| Scenario | What Happens |
|----------|-------------|
| **New prompt for existing agent** | Bump version in YAML (e.g., `1.0.0` → `1.1.0`). New row inserted. Existing projects continue on old version until explicitly upgraded. |
| **New tool added** | Bump version. New row with updated manifest. |
| **Model change** | Bump version. New row. |
| **Deprecation** | Set `deprecated_at` + `deprecation_message` in seed YAML. Seed service updates existing row. |
| **Retirement** | Set `enabled: false` on deprecated agent. Existing projects keep cached definition. |

### Platform vs Project-Scoped

- **Platform agents** (`invocation_tier: platform`): Director, Supervisor. Auto-assigned to every project. Cannot be removed from a project.
- **System agents** (`invocation_tier: system`): Planner, Summarizer, Verifier. Invoked by other agents or the Dispatch Engine. Not directly user-facing.
- **User agents** (`invocation_tier: user`): All specialist agents. Assigned to projects by owners or templates.

---

## SECTION 8: Future-Proofing

### Adding a new category (e.g., "finance")

1. Add `Prompts/Categories/finance.md` — category prompt
2. Add `Tools/Finance/` folder with domain tools
3. Register tools in `ToolRegistration.cs`
4. Add seed YAMLs: `finance.analyst.yaml`, `finance.compliance-checker.yaml`, etc.
5. No structural changes needed. Categories are strings in the catalog, validated against the set of available prompt files.

### Adding a subcategory (e.g., "security.cloud")

1. Add seed YAMLs with dotted names: `security.cloud.scanner.yaml`
2. Add `Tools/Security/` tools (or a subfolder `Tools/Security/Cloud/` if many)
3. The category prompt (`security.md`) covers all subcategories
4. No code changes — subcategories are naming conventions, not structural

### Custom AIAgent subclass (not ChatClientAgent)

Supported via `AgentFactory`:

```csharp
// In AgentFactory.Create(), check catalog for agent_runtime_type:
if (catalog.RuntimeType == "delegating")
{
    var inner = CreateChatClientAgent(catalog, project, ...);
    return new CustomDelegatingAgent(inner, customBehavior);
}
```

Add a `RuntimeType` field to `AgentCatalog` (default: `"chat_client"`). The factory dispatches on this field. New runtime types require code changes — by design (Principle 21: Explicit Over Implicit).

### A2A (remote) agents

A2A agents are represented in the catalog with `runtime_type: "a2a"`:

```yaml
agent_name: external.compliance-checker
runtime_type: a2a
a2a_endpoint: https://compliance-agent.internal.investec.com/.well-known/agent.json
```

The `AgentFactory` creates an `A2AAgent` (MAF's built-in A2A client) instead of `ChatClientAgent`. Tools, prompts, and constraints are still defined in the catalog for visibility, but execution is delegated to the remote agent.

### Custom IChatClient pipeline

Add a `pipeline_override` field to the catalog. The `ChatClientFactory` checks for overrides before using the shared pipeline:

```csharp
public IChatClient GetOrCreate(string provider, string model, string? pipelineOverride = null)
{
    var key = pipelineOverride is not null
        ? $"{provider}:{model}:{pipelineOverride}"
        : $"{provider}:{model}";
    return _clients.GetOrAdd(key, _ => BuildPipeline(provider, model, pipelineOverride));
}
```

Custom pipelines are rare — the default covers 95% of cases. Custom pipelines might skip content safety (for internal-only agents) or add domain-specific middleware.

### 50+ agents across 10 categories

The structure holds. Tools are domain-organised (not per-agent), so 50 agents sharing 20 tool folders is manageable. Seed YAMLs are flat files — no nesting. The `ToolRegistry` is a dictionary lookup — O(1) per tool. The `ChatClientFactory` caches pipelines — agent construction is cheap.

### Multi-tenant

The `AgentCatalog` gains an optional `TenantId` column (null = platform-wide). Tenant-specific agents are only visible to that tenant's projects. The `AgentCatalogResolver` filters by tenant. Prompt Layers 1-2 can be tenant-overridable by adding a `Prompts/Tenants/{tenantId}/` overlay.

---

## Summary: Complete Folder Tree

```
src/AgenticWorkforce.Agents/
├── AgenticWorkforce.Agents.csproj
├── DependencyInjection.cs
├── Runtime/
│   ├── IAgentRuntime.cs
│   ├── AgentRuntime.cs
│   ├── IAgentFactory.cs
│   ├── AgentFactory.cs
│   ├── IChatClientFactory.cs
│   └── ChatClientFactory.cs
├── Catalog/
│   ├── IAgentCatalogResolver.cs
│   ├── AgentCatalogResolver.cs
│   ├── AgentSeedService.cs
│   └── Seeds/                          # 16+ YAML files
│       ├── project.director.yaml
│       ├── project.planner.yaml
│       ├── project.supervisor.yaml
│       └── ...
├── Prompts/
│   ├── IPromptAssembler.cs
│   ├── PromptAssembler.cs
│   ├── Organization/                   # 4 global .md files
│   │   ├── principles.md
│   │   ├── coding-standards.md
│   │   ├── security-posture.md
│   │   └── communication-style.md
│   └── Categories/                     # 5 category .md files
│       ├── project.md
│       ├── software.md
│       ├── research.md
│       ├── security.md
│       └── system.md
├── Tools/
│   ├── IToolRegistry.cs
│   ├── ToolRegistry.cs
│   ├── IFileScopedTool.cs
│   ├── ApprovalRequiredAIFunction.cs
│   ├── ToolRegistration.cs
│   ├── Common/                         # 6 cross-cutting tools
│   ├── Project/                        # 15 project management tools
│   ├── Security/                       # 4 security tools
│   ├── Research/                       # 3 research tools
│   ├── Software/                       # 3 software tools
│   ├── Supervisor/                     # 2 supervisor tools
│   └── Mcp/
│       ├── IMcpToolResolver.cs
│       └── McpToolResolver.cs
├── Context/
│   ├── IProjectContextProviderFactory.cs
│   ├── ProjectContextProviderFactory.cs
│   ├── ProjectContextProvider.cs
│   ├── IContextAssembler.cs
│   └── ContextAssembler.cs
├── Middleware/
│   ├── BudgetEnforcingChatClient.cs
│   ├── AuditingChatClient.cs
│   ├── ContentSafetyChatClient.cs
│   └── CostTrackingChatClient.cs
└── Verification/
    ├── IVerifier.cs
    ├── VerificationPipeline.cs
    ├── SchemaVerifier.cs
    ├── RuleVerifier.cs
    └── AgentVerifier.cs
```

**Total: ~55 entries** (files + folders). Scales to 10 categories and 50+ agents without restructuring.

---

## Agent Registration Checklist

| # | Step | Files |
|---|------|-------|
| 1 | Create seed YAML | `Catalog/Seeds/{name}.yaml` |
| 2 | Write category prompt (if new category) | `Prompts/Categories/{category}.md` |
| 3 | Implement tools (if new) | `Tools/{Domain}/{Tool}.cs` |
| 4 | Register tools in DI | `ToolRegistration.cs` |
| 5 | Deploy — seed service auto-inserts on startup | — |
| 6 | Test with `enabled: false` via admin API | — |
| 7 | Enable via admin API | — |
| 8 | Assign to projects (or via template) | — |

---

## Key Differences from Mission Control Prototype

| Aspect | Prototype (Python) | Production (C#) | Rationale |
|--------|-------------------|-----------------|-----------|
| Agent definitions | YAML files on disk | YAML seeds → DB catalog | DB enables versioning, per-project overrides, runtime queries |
| Agent construction | Direct class instantiation | `AgentFactory` + sealed `ChatClientAgent` | MAF constraint; composition over inheritance |
| Prompt files | `config/prompts/` filesystem | Embedded resources in Agents project | Deployed with the binary; no filesystem dependency in containers |
| Agent system prompt | Disk file (Layer 3) | DB field on `AgentCatalog` (seeded from YAML) | Enables versioning, A/B testing, runtime editing |
| Tool registration | Decorator-based auto-discovery | Explicit `ToolRegistry.Register()` | P21: Explicit Over Implicit |
| Agent naming | `{category}.{subcategory?}.{name}.agent` | `{category}.{subcategory?}.{name}` | Dropped `.agent` suffix — redundant in typed catalog |
| Categories | Filesystem folders | String field + prompt file convention | Same set of categories, different enforcement mechanism |
| Dispatch loop | Python async loop | Durable Task orchestrator | Crash recovery, pod restart durability |
| Supervisor | Session followup | Explicit Durable Task activity | Auditable, versioned, structured output |
| Platform agents | `platform/` category | `invocation_tier: platform` on any category | Decouples "always-on" from category |
