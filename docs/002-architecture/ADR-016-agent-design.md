# ADR-016: Agent Design — Catalog, Prompts, Tools, Constraints, and Runtime Model

**Status:** Accepted
**Date:** 2026-05-12
**Decision Makers:** Architecture team
**Research:** [R19-response-agent-design.md](../098-research/R19-response-agent-design.md)
**Complements:** [ADR-003 — Agent Model Design](ADR-003-agent-model-design.md) (the MAF integration pattern), [ADR-010 — Context Assembly](ADR-010-context-assembly.md) (prompt layering and context injection)

---

## Context

ADR-003 established the MAF integration pattern (sealed `ChatClientAgent`, `AgentFactory`, `AIContextProvider`, middleware pipeline). This ADR defines the complete agent design: catalog schema, prompt assembly, tool scoping, file access control, per-project customisation, lifecycle, and visibility.

## Decision

**Database-driven agent catalog with 5-layer prompt assembly, explicit tool manifests, file scope enforcement, per-project customisation via `ProjectAgent`, and immutable versioned definitions.**

---

### 1. Agent Catalog Entity

The agent catalog is the registry of all available agent definitions. Each entry is a versioned, immutable snapshot — changes create a new version, never mutate in place.

```csharp
public class AgentCatalog : EntityBase
{
    // Identity
    public string AgentName { get; set; } = null!;       // e.g., "security.webapp.scanner"
    public string AgentType { get; set; } = null!;        // horizontal | vertical
    public string Category { get; set; } = null!;         // project | software | research | security | system
    public string Version { get; set; } = null!;          // semver: "1.2.0"
    public string Description { get; set; } = null!;
    public string[] Keywords { get; set; } = [];           // for routing/discovery

    // Model configuration
    public string ModelProvider { get; set; } = null!;     // foundry-anthropic | azure-openai | anthropic-direct
    public string ModelName { get; set; } = null!;         // claude-sonnet-4-6 | gpt-4o-mini
    public float Temperature { get; set; }
    public int MaxOutputTokens { get; set; }
    public int? MaxThinkingTokens { get; set; }            // null = thinking disabled

    // System prompt (Layer 3 — versioned via PromptVersion table)
    public string SystemPrompt { get; set; } = null!;

    // Tool manifest — explicit list of allowed tool names
    [Column(TypeName = "jsonb")]
    public string ToolManifest { get; set; } = "[]";       // List<ToolBinding>

    // File scope — read/write path restrictions
    [Column(TypeName = "jsonb")]
    public string FileScope { get; set; } = "{}";          // FileScopePolicy

    // Interface contract — typed input/output schemas
    [Column(TypeName = "jsonb")]
    public string InterfaceContract { get; set; } = "{}";  // InterfaceContract

    // Constraints
    public decimal MaxBudgetUsd { get; set; } = 1.0m;
    public int MaxInputLength { get; set; } = 32000;
    public int MaxToolCalls { get; set; } = 50;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 600;

    // Execution
    public string ExecutionMode { get; set; } = "sandbox"; // sandbox | local (dev only)
    public string? ExecutionImage { get; set; }             // container image for sandbox

    // Invocation
    public string InvocationTier { get; set; } = "user";   // user | system | platform
    public bool ChatEnabled { get; set; }
    public bool ProducesArtifact { get; set; }
    public string? ArtifactType { get; set; }

    // Visibility & lifecycle
    public string Visibility { get; set; } = "public";     // public | internal | private
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationMessage { get; set; }

    // Format versioning
    public string FormatVersion { get; set; } = "1.0";
}
```

**JSONB sub-types:**

```csharp
public record ToolBinding(
    string Name,                        // tool registry key: "security.code.scan"
    bool RequiresApproval = false,      // surfaces HITL gate before execution
    string? McpServer = null            // if from MCP: server name
);

public record FileScopePolicy(
    string[] ReadPaths = default!,      // ["@workspace/target/", "docs/"]
    string[] WritePaths = default!      // ["@workspace/evidence/"]
    // @workspace/ prefix = resolved to Dynamic Sessions /mnt/data
    // relative paths = resolved within project repo root
);

public record InterfaceContract(
    Dictionary<string, string>? Input,  // { "findings": "array", "scope": "string" }
    Dictionary<string, string>? Output, // { "report": "object", "score": "number" }
    string? ResponseSchema,             // JSON Schema for structured output
    string? SchemaName,                 // e.g., "VulnerabilityReport"
    string? SchemaDescription
);
```

### 2. Agent Categories and Types

| Type | Meaning | Example |
|------|---------|---------|
| `horizontal` | Cross-domain capability, reusable across project types | Planner, Verifier, Summarizer, Knowledge Officer |
| `vertical` | Domain-specific specialist | Security Scanner, Code Analyst, Research Strategist |

| Category | Prompt Layer 2 File | Agents |
|----------|-------------------|--------|
| `project` | `prompts/categories/project.md` | director, planner, supervisor |
| `software` | `prompts/categories/software.md` | code analyst, architecture reviewer, quality verifier |
| `research` | `prompts/categories/research.md` | strategist, searcher, analyst, synthesizer |
| `security` | `prompts/categories/security.md` | scanner, triage, reporter |
| `system` | `prompts/categories/system.md` | summarization, verification, knowledge officer |

Categories are fixed in code (not user-definable) because each maps to a prompt file and implies a set of conventions. New categories require a code change + prompt file + review.

### 3. Prompt Assembly

Five layers, assembled into a single `Instructions` string for `ChatClientAgent`:

```
Layer 1: Organization prompts (disk — global)
    ├── principles.md
    ├── coding_standards.md
    └── security_posture.md
Layer 2: Category prompt (disk — per agent category)
    └── {category}.md
Layer 3: Agent system prompt (DB — versioned)
    └── AgentCatalog.SystemPrompt (or variant for chat mode)
Layer 4: Project brief (DB — per project)
    └── Project.Brief
Layer 5: User prompt (DB — per agent per project)
    └── ProjectAgent.UserPrompt
```

**Implementation:**

```csharp
public sealed class PromptAssembler
{
    private readonly Dictionary<string, string> _orgPrompts;      // loaded once at startup
    private readonly Dictionary<string, string> _categoryPrompts; // loaded once at startup

    public PromptAssembler(IConfiguration config)
    {
        _orgPrompts = LoadOrganizationPrompts(config);
        _categoryPrompts = LoadCategoryPrompts(config);
    }

    public string Assemble(
        AgentCatalog agent,
        string? projectBrief,
        string? userPrompt,
        string? promptVariant = null)
    {
        var layers = new List<string>();

        // Layer 1: Organization (all .md files, sorted)
        layers.AddRange(_orgPrompts.Values);

        // Layer 2: Category
        if (_categoryPrompts.TryGetValue(agent.Category, out var catPrompt))
            layers.Add(catPrompt);

        // Layer 3: Agent system prompt (or variant)
        layers.Add(promptVariant ?? agent.SystemPrompt);

        // Layer 4: Project brief
        if (!string.IsNullOrEmpty(projectBrief))
            layers.Add($"## Project Brief\n\n{projectBrief}");

        // Layer 5: User prompt (additive, per agent per project)
        if (!string.IsNullOrEmpty(userPrompt))
            layers.Add($"## Project-Specific Instructions\n\n{userPrompt}");

        return string.Join("\n\n", layers);
    }
}
```

**Layers 1-3 are static** (set at agent construction via `Instructions`).
**Layers 4-5 are dynamic** but set once per project context — they don't change per turn. They're assembled at agent construction time, not via `AIContextProvider`.

**Per-turn dynamic context** (PCD, learnings, task definition, history) comes through `AIContextProvider.InvokingAsync()` as defined in ADR-010. This is separate from prompt assembly — it's context, not instructions.

**Prompt variants:** Chat mode uses a different system prompt than execution mode. The `AgentFactory` loads the variant from `PromptVersion` or a separate `ChatPrompt` field. Two `ChatClientAgent` instances are created from the same catalog entry — one for execution, one for chat — sharing the same `IChatClient` pipeline.

### 4. Tool Manifest and Scoping

**ToolRegistry — maps tool names to implementations with execution domain enforcement (P22):**

```csharp
public enum ExecutionDomain { Sandbox, Platform }

public sealed class ToolRegistry
{
    private readonly Dictionary<string, (Func<IServiceProvider, AITool> Factory, ExecutionDomain Domain)> _tools = new();

    // Default: Sandbox (containerized). All new tools run in containers unless explicitly justified.
    public void Register(string name, Func<IServiceProvider, AITool> factory,
        ExecutionDomain domain = ExecutionDomain.Sandbox)
        => _tools[name] = (factory, domain);

    public IList<AITool> Resolve(
        IEnumerable<ToolBinding> manifest,
        IServiceProvider sp,
        FileScopePolicy fileScope)
    {
        var tools = new List<AITool>();
        foreach (var binding in manifest)
        {
            if (!_tools.TryGetValue(binding.Name, out var entry))
                throw new InvalidOperationException(
                    $"Tool '{binding.Name}' not found in registry. Agent catalog references a tool that doesn't exist.");

            var tool = entry.Factory(sp);

            // Wrap with file scope enforcement if tool accesses files
            if (tool is IFileScopedTool scopedTool)
                scopedTool.SetScope(fileScope);

            // Wrap with approval gate if required
            if (binding.RequiresApproval)
                tool = new ApprovalRequiredAIFunction((AIFunction)tool);

            tools.Add(tool);
        }
        return tools;
    }
}
```

**File scope enforcement — in the tool implementation, not middleware:**

```csharp
public sealed class SandboxedFileReadTool : IFileScopedTool
{
    private FileScopePolicy _scope = new();

    public void SetScope(FileScopePolicy scope) => _scope = scope;

    [Description("Read a file from the project workspace")]
    public async Task<string> ReadFileAsync(
        [Description("Relative path within the workspace")] string path)
    {
        // Enforce read scope BEFORE sending to sandbox
        if (!_scope.ReadPaths.Any(allowed => path.StartsWith(allowed)))
            throw new SecurityException($"Access denied: '{path}' is outside read scope [{string.Join(", ", _scope.ReadPaths)}]");

        if (path.Contains("..") || Path.IsPathRooted(path))
            throw new SecurityException("Access denied: path traversal attempt");

        return await _sessionClient.ExecuteAsync(_sessionId, $"cat '{path}'");
    }
}
```

**Tool call rejected if not in manifest:** If an agent somehow tries to call a tool not in its `ChatOptions.Tools`, MAF's `FunctionInvokingChatClient` will not find it and the call fails. This is enforce-by-construction — the tool simply isn't registered. Per Principle 14 (Secure by Default): empty manifest = zero tools.

**MCP tools integrate via the manifest:**

```csharp
// During agent construction, if ToolBinding.McpServer is set:
if (binding.McpServer is not null)
{
    var mcpClient = _mcpClientFactory.Get(binding.McpServer);
    var mcpTools = await mcpClient.ListToolsAsync();
    var mcpTool = mcpTools.FirstOrDefault(t => t.Name == binding.Name)
        ?? throw new InvalidOperationException($"MCP tool '{binding.Name}' not found on server '{binding.McpServer}'");
    tools.Add(mcpTool);
}
```

### 5. AgentFactory — Runtime Construction

```csharp
public sealed class AgentFactory
{
    private readonly IChatClientFactory _clientFactory;
    private readonly PromptAssembler _promptAssembler;
    private readonly ToolRegistry _toolRegistry;
    private readonly IProjectContextProviderFactory _contextProviderFactory;

    public AIAgent Create(
        AgentCatalog catalog,
        ProjectContext project,
        ProjectAgent? projectAgent = null,
        string? promptVariant = null)
    {
        // 1. Resolve IChatClient for this agent's model
        IChatClient client = _clientFactory.GetOrCreate(catalog.ModelProvider, catalog.ModelName);

        // 2. Assemble prompt (5 layers)
        string instructions = _promptAssembler.Assemble(
            catalog,
            projectBrief: project.Brief,
            userPrompt: projectAgent?.UserPrompt,
            promptVariant: promptVariant);

        // 3. Resolve tools from manifest + file scope
        var fileScope = JsonSerializer.Deserialize<FileScopePolicy>(catalog.FileScope)!;
        var manifest = JsonSerializer.Deserialize<List<ToolBinding>>(catalog.ToolManifest)!;
        var tools = _toolRegistry.Resolve(manifest, _serviceProvider, fileScope);

        // 4. Create context provider for per-turn context injection (ADR-010)
        var contextProvider = _contextProviderFactory.Create(project, catalog);

        // 5. Build agent
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
            UseProvidedChatClientAsIs = true,  // CRITICAL: we composed our own pipeline
            RequirePerServiceCallChatHistoryPersistence = true, // crash recovery + audit
            AIContextProviderFactory = _ => contextProvider,
        });
    }

    public AIAgent CreateForChat(AgentCatalog catalog, ProjectContext project, ProjectAgent? projectAgent)
        => Create(catalog, project, projectAgent, promptVariant: "chat");
}
```

**Key MAF settings:**
- **`UseProvidedChatClientAsIs = true`** — we compose our own `IChatClient` pipeline (Budget → Audit → FunctionInvocation → ContentSafety → OTel → Provider). Without this flag, MAF wraps our pipeline again with a second `FunctionInvokingChatClient`, causing double tool charges and broken middleware ordering.
- **`RequirePerServiceCallChatHistoryPersistence = true`** — saves intermediate tool results between each service call inside a tool loop. Required for crash recovery and audit compliance in a regulated bank.

**`IChatClient` pipeline shared per `(provider, model)`:**

```csharp
public sealed class ChatClientFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new();

    public IChatClient GetOrCreate(string provider, string model)
    {
        var key = $"{provider}:{model}";
        return _clients.GetOrAdd(key, _ => BuildPipeline(provider, model));
    }

    private IChatClient BuildPipeline(string provider, string model)
    {
        IChatClient raw = provider switch
        {
            "foundry-anthropic" => _anthropicFoundryClient.AsIChatClient(model),
            "azure-openai" => _azureOpenAIClient.GetChatClient(model).AsIChatClient(),
            "anthropic-direct" => _anthropicClient.AsIChatClient(model),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };

        return raw.AsBuilder()
            .Use((inner, sp) => new BudgetEnforcingChatClient(inner, sp.GetRequiredService<IBudgetService>()))
            .Use((inner, sp) => new AuditingChatClient(inner, sp.GetRequiredService<ChannelWriter<AuditRecord>>()))
            .UseFunctionInvocation()
            .Use((inner, sp) => new ContentSafetyChatClient(inner, sp.GetRequiredService<ContentSafetyClient>()))
            .UseOpenTelemetry(sourceName: "AgenticWorkforce.Agents", configure: o => o.EnableSensitiveData = false)
            .Build(_serviceProvider);
    }
}
```

### 6. Per-Project Agent Customisation

```csharp
public class ProjectAgent : ProjectScopedEntity
{
    public Guid AgentCatalogId { get; set; }
    public AgentCatalog AgentCatalog { get; set; } = null!;

    // Role within project (from template)
    public string Role { get; set; } = null!;             // supervisor | researcher | qa | reporter | specialist

    // Layer 5: user prompt (additive, per agent per project)
    public string? UserPrompt { get; set; }
    public int UserPromptVersion { get; set; } = 1;        // incremented on each edit

    // Per-project overrides
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    [Column(TypeName = "jsonb")]
    public string? CustomConstraints { get; set; }         // override budget, timeout, etc.
    [Column(TypeName = "jsonb")]
    public string? AdditionalTools { get; set; }           // extra tools for this project only
    [Column(TypeName = "jsonb")]
    public string? RestrictedTools { get; set; }           // tools to remove for this project
}
```

**Merging at runtime:**

```csharp
// In AgentFactory.Create():
// Merge per-project tool overrides
if (projectAgent?.AdditionalTools is not null)
{
    var additional = JsonSerializer.Deserialize<List<ToolBinding>>(projectAgent.AdditionalTools)!;
    manifest.AddRange(additional);
}
if (projectAgent?.RestrictedTools is not null)
{
    var restricted = JsonSerializer.Deserialize<List<string>>(projectAgent.RestrictedTools)!;
    manifest.RemoveAll(t => restricted.Contains(t.Name));
}

// Merge per-project constraint overrides
if (projectAgent?.CustomConstraints is not null)
{
    var overrides = JsonSerializer.Deserialize<ConstraintOverrides>(projectAgent.CustomConstraints)!;
    if (overrides.MaxBudgetUsd.HasValue) catalog.MaxBudgetUsd = overrides.MaxBudgetUsd.Value;
    if (overrides.TimeoutSeconds.HasValue) catalog.TimeoutSeconds = overrides.TimeoutSeconds.Value;
}
```

### 7. Agent Visibility

**Roster format (what the Planner sees):**

```csharp
public record RosterEntry(
    string AgentName,
    string AgentVersion,
    string Description,
    string ModelName,
    string[] Tools,
    InterfaceContract Interface,
    AgentConstraints Constraints
);
```

The Planner agent receives the roster as context — it sees what agents are available, their capabilities, tools, input/output contracts, and constraints. This allows it to create tasks with correct agent assignments.

**Catalog browser (what the UI shows):**
- Name, description, category, type, version, keywords
- Model (name only, not provider details)
- Tools (names and descriptions)
- Constraints (budget, timeout)
- Whether chat-enabled
- Usage stats (projects using it, total cost, success rate)
- System prompt: visible to Owner and Platform Admin only (security-sensitive)

**Agent-as-tool (when one agent calls another):**
Uses `agent.AsAIFunction()` — the inner agent's `Name` becomes the tool name, `Description` becomes the tool description. The inner agent runs its own session independently.

### 8. Agent Lifecycle

| Phase | Trigger | What Happens |
|-------|---------|--------------|
| **Draft** | `POST /api/v1/admin/catalog` | Created with `enabled: false`. Can be tested in workshop. |
| **Published** | Admin sets `enabled: true` | Available for project team assignment. |
| **Versioned** | Prompt, tool, or model change | New row with incremented version. Old version retained. Projects using old version continue until explicitly upgraded. |
| **Deprecated** | Admin sets `deprecated_at` + message | Still functional but UI shows warning. No new project assignments. |
| **Retired** | Admin sets `enabled: false` on deprecated agent | Cannot be assigned to new projects. Existing projects continue with cached definition. |

**Version bumps required for:**
- System prompt change → new version + PromptVersion audit entry
- Tool manifest change → new version
- Model change → new version
- Constraint change → new version

**Workshop/sandbox testing:** `POST /api/v1/admin/catalog/{agentId}/test` runs the agent with test input in a sandbox, returns output without persisting. For prompt iteration before publishing.

---

## Consequences

- Agent catalog is versioned and immutable — changes create new versions, never mutate
- `UseProvidedChatClientAsIs = true` is mandatory — forgetting it causes double function invocation loops
- `RequirePerServiceCallChatHistoryPersistence = true` is mandatory for crash recovery and audit
- `IChatClient` pipeline is shared per `(provider, model)` — agent construction is cheap (just options + shared client)
- File scope is enforced in tool implementations, not middleware — each tool checks its own scope before executing
- Tool manifest is explicit (Principle 14: Secure by Default) — empty manifest = zero tools
- Per-project tool overrides (add/restrict) allow flexibility without changing the catalog
- Prompt variants (system vs chat) create separate agent instances sharing the same pipeline
- Only one `AIContextProvider` per agent in MAF 1.5.0 — compose multiple concerns into a single provider
- Categories are fixed in code — new categories require a code change + prompt file

### Principle Compliance

- **P14 Secure by Default:** Empty tool manifest = zero tools. New agents default to `enabled: false`. File scope defaults to empty (no access). Tools require explicit allowlisting per agent.
- **P15 Backend Owns All Logic:** Agent construction, prompt assembly, tool scoping, file scope enforcement, and all execution decisions are server-side. Clients display agent info — they don't construct agents.
- **P16 Single Source of Truth:** `AgentCatalog` table is the single authoritative source for agent definitions. Disk-based prompts (Layers 1-2) are deployed with the application. In-memory agent instances are ephemeral derived objects.
- **P17 Human Authority:** Humans control agent lifecycle (enable/disable/deprecate). User prompts (Layer 5) are human-authored direction. Agents with `RequiresApproval` tools pause for human approval. System prompts are versioned for audit.
- **P18 Idempotency:** Agent construction is stateless and idempotent. The `IChatClient` pipeline is created once and reused. `AgentFactory.Create()` can be called multiple times for the same catalog entry — produces identical agents.
- **P19 Bounded Resource Usage:** Every agent has explicit bounds: `MaxBudgetUsd`, `MaxInputLength`, `MaxToolCalls`, `MaxRetries`, `TimeoutSeconds`. No unbounded agents exist.
- **P20 Version Everything:** Agent definitions are versioned with `(AgentName, Version)`. System prompts are versioned in `PromptVersion` table. User prompts are versioned via `UserPromptVersion` on `ProjectAgent`.
- **P21 Explicit Over Implicit:** Tool registration is explicit via `ToolRegistry`. No auto-discovery of tools. File scope is explicitly declared. Model assignment is explicit per agent — no automatic routing or fallback.
- **P22 Container-First Execution:** All tools that make network calls or access the filesystem are registered as Sandbox (containerized) by default. Only `project.*` tools (internal DB queries) are Platform (in-process). New tools default to Sandbox. `ToolRegistry.Register()` requires explicit `ExecutionDomain.Platform` with justification to register a tool as in-process.
