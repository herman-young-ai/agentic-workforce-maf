# Phase 7: Agent Catalog & Tools

**Status:** Not Started
**Depends On:** Phase 6 (Agent Runtime)
**Verification:** Agent catalog seeded to DB on startup; tools resolve from manifest; verification pipeline runs.

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

Populate the agent ecosystem: seed YAML definitions for all 16 agents into the database at startup, implement all platform tools (in-process) and sandbox tool stubs, build the 3-tier verification pipeline, and wire up MCP tool resolution. After this phase, the system has a full agent team ready to execute tasks.

The Agents project depends **only on Domain** (per the layer graph in AGENTS.md). It never references EF Core, Npgsql, or `AppDbContext` directly — all persistence flows through Domain repository interfaces. The seeder lives in **Infrastructure** alongside `StubModelPricingSeeder`.

---

## Phase Split

Phase 7 lands as five sub-deliveries, each gated by green build + green tests + no CQI regression.

| Sub-phase | Scope | Files | Verification |
|-----------|-------|-------|--------------|
| **7a** | `AgentCatalog` schema additions + migration + `AgentSeedService` (Infrastructure) + 1 proof YAML + tests | ~6 | Worker startup seeds 1 agent; idempotent on re-run; architecture tests still green. |
| **7b** | Remaining 15 YAML seeds + canonical YAML→jsonb mapping spec + per-YAML schema fixture test | ~17 | `AgentCatalogs` table has 16 rows after Worker boot; every jsonb column parses through the shared canonical reader. |
| **7c** | 15 Platform tools (`project.*`) + `IPlatformTool` marker + registration helper | ~17 | All Platform tools registered; architecture test `PlatformTools_DoNotDependOnHttpOrFileOrProcess` covers them. |
| **7d** | 2 supervisor Platform tools + 18 sandbox tool stubs (throwing) | ~20 | Sandbox stubs throw `SandboxUnavailableException`; ToolRegistry resolves all 35 tools. |
| **7e** | 3-tier `VerificationPipeline` + MCP resolver stub + integration tests | ~10 | Tier 1 rejects malformed JSON; Tier 2 enforces rules; Tier 3 calls `system.verifier` via real `IAgentRuntime`; recursion guard verified. |

Architecture tests from Phase 6 must remain green after each sub-phase. Any new arch tests (e.g., seeded-YAML invariants) land in 7b.

---

## Architecture / Boundary Reminders

```
Api ─┐                                Worker ─┐
     ├── Domain.IAgentCatalogRepository ──── Infrastructure.CachingAgentCatalogRepository
     │                                       └── Infrastructure.AgentCatalogRepository (EF Core)
     │                                       └── Infrastructure.AgentSeedService (IHostedService — Phase 7)
     ├── Domain.IAgentRuntime ────────────── Agents.AgentRuntime (Phase 6)
     │                                       └── Agents.Tools.IToolRegistry
     │                                       └── Agents.Verification.IVerifier
     └── Domain.IProjectRepository, etc.
```

**Rules carried over from Phase 6 (do not violate):**

- `AgenticWorkforce.Agents` references only `AgenticWorkforce.Domain`. Architecture test `Agents_HasNoEfCoreOrNpgsqlDependency` will fail any reach into `AppDbContext`.
- Caching of `IAgentCatalogRepository` lives in `Infrastructure` (`CachingAgentCatalogRepository`). The seeder runs **before** any agent execution warms the cache; on update it removes the affected cache entries directly via the same `IMemoryCache` instance.
- Every async method takes `CancellationToken`. The Phase-6 `SourceConventionTests` arch test enforces this.

---

## 1. AgentCatalog Schema Additions

The current `AgentCatalog` entity stores most YAML sections as `jsonb`. Two YAML fields used by the verification pipeline are NOT yet columns and must be added:

```csharp
public class AgentCatalog : EntityBase
{
    // ... existing fields ...

    /// <summary>True if this agent emits an artifact subject to Tier 3 (AgentVerifier) review.</summary>
    public bool ProducesArtifact { get; set; }

    /// <summary>Stable artifact-type discriminator (e.g. "VulnerabilityReport"). Null when ProducesArtifact = false.</summary>
    public string? ArtifactType { get; set; }
}
```

**Migration:** `Phase7AgentCatalogProducesArtifact` adds two columns:

```sql
ALTER TABLE agent_catalogs ADD COLUMN produces_artifact boolean NOT NULL DEFAULT false;
ALTER TABLE agent_catalogs ADD COLUMN artifact_type varchar(128) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_agent_catalogs_agent_name ON agent_catalogs (agent_name);
```

The unique index on `agent_name` closes a latent gap: the seeder's "first-or-default by name" pattern needs uniqueness to be guaranteed by the database, not by convention.

---

## 2. Agent Seeding

### Location and dependencies (Principle 4 — Wrap the Core)

`AgentSeedService` is a hosted service in **`AgenticWorkforce.Infrastructure/Services/`**. It consumes `IAgentCatalogRepository` and `IPromptVersionRepository` — never `AppDbContext` directly. Mirrors `StubModelPricingSeeder` from Phase 6.

Embedded YAMLs are owned by the `AgenticWorkforce.Agents` project (so the Agents project remains the canonical authority on agent definitions). The seeder reads them from the Agents assembly's manifest resources:

```csharp
var stream = typeof(AgenticWorkforce.Agents.AgentsAssemblyMarker).Assembly
    .GetManifestResourceStream("AgenticWorkforce.Agents.Catalog.Seeds." + name);
```

A small `AgentsAssemblyMarker` type in `AgenticWorkforce.Agents` exists solely as a type token for the manifest lookup — public so Infrastructure can reference it. No business logic.

### IsNewer / version semantics

`agent_version` is `Major.Minor.Patch`. Strictly numeric. Comparison parses each segment as an integer and compares the tuple lexicographically. **Equal versions skip the update entirely** (so editing a YAML's prompt without bumping the version is a no-op — operators must bump). A YAML with a version that parses-failed crashes the seeder at startup (Principle 8 — fail fast). The seeder does not "best-effort" past a bad YAML.

```csharp
internal readonly record struct AgentSemver(int Major, int Minor, int Patch) : IComparable<AgentSemver>
{
    public static AgentSemver Parse(string raw) { /* throws FormatException on malformed input */ }
    public int CompareTo(AgentSemver other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
}
```

### Single round-trip seeding

The plan's original "FirstOrDefaultAsync per YAML" is replaced by one `ListAllAsync()` + in-memory dictionary lookup. 16 YAMLs → 1 DB round trip on startup, plus inserts/updates batched in a single `SaveChanges`.

### PromptVersion history (Principle 20 — Version Everything)

When seeding **changes** `SystemPrompt` on an existing agent, the seeder writes a new `PromptVersion` row (linking the prior version) before updating `AgentCatalog.SystemPrompt`. In-flight agent executions hold their own `PromptVersion` reference (Phase 8 wiring) and therefore see the prompt they started with. Phase 7 establishes the history; Phase 8 consumes it.

### AgentSeedService

```csharp
// Path: src/AgenticWorkforce.Infrastructure/Services/AgentSeedService.cs
internal sealed class AgentSeedService(
    IServiceScopeFactory scopes,
    IAgentSeedSource source,                    // returns IReadOnlyList<AgentSeedDefinition>
    ILogger<AgentSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAgentCatalogRepository>();
        var prompts = scope.ServiceProvider.GetRequiredService<IPromptVersionRepository>();
        var cache   = scope.ServiceProvider.GetRequiredService<IMemoryCache>();

        var existing = (await catalog.ListAllAsync(ct))
            .ToDictionary(a => a.AgentName, StringComparer.Ordinal);

        foreach (var def in source.Load())
        {
            var newVersion = AgentSemver.Parse(def.AgentVersion);

            if (!existing.TryGetValue(def.AgentName, out var current))
            {
                await catalog.AddAsync(AgentSeedMapper.ToEntity(def), ct);
                LogSeeded(logger, def.AgentName, def.AgentVersion, null);
                continue;
            }

            var currentVersion = AgentSemver.Parse(current.AgentVersion!);
            if (newVersion.CompareTo(currentVersion) <= 0) continue;

            if (!string.Equals(current.SystemPrompt, def.SystemPrompt, StringComparison.Ordinal))
                await prompts.AppendAsync(current.Id, current.SystemPrompt, def.AgentVersion, ct);

            AgentSeedMapper.Update(current, def);
            await catalog.UpdateAsync(current, ct);

            // CachingAgentCatalogRepository write paths already evict the matching keys; this
            // explicit eviction guards against direct registrations of AgentCatalogRepository
            // bypassing the decorator (e.g. integration test scopes).
            cache.Remove($"agent:id:{current.Id}");
            cache.Remove($"agent:name:{current.AgentName}");

            LogUpdated(logger, def.AgentName, def.AgentVersion, null);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static readonly Action<ILogger, string, string, Exception?> LogSeeded =
        LoggerMessage.Define<string, string>(LogLevel.Information,
            new EventId(1, nameof(LogSeeded)),
            "Seeded agent {AgentName} v{Version}");

    private static readonly Action<ILogger, string, string, Exception?> LogUpdated =
        LoggerMessage.Define<string, string>(LogLevel.Information,
            new EventId(2, nameof(LogUpdated)),
            "Updated agent {AgentName} -> v{Version}");
}
```

`IAgentSeedSource` is an internal Infrastructure abstraction with one production implementation (`EmbeddedYamlAgentSeedSource`) and a test implementation that hands in fixtures — keeps the seeder logic testable without the embedded-resource detour.

### Hosted-service ordering

`AgentSeedService` is registered **before** any service whose construction can trigger agent resolution. Worker host order:

```
StubModelPricingSeeder   (Phase 6)
AgentSeedService         (Phase 7)
LlmCallDrainService      (Phase 6)
... future workers
```

Registration order in `AddInfrastructure` (Infrastructure DI) preserves this; ASP.NET Core invokes `IHostedService.StartAsync` in registration order.

---

## 3. YAML → jsonb Mapping (Canonical)

Every YAML section maps to exactly one storage location. The same canonical reader is used by the seeder (write) **and** by every runtime consumer (read) so the JSON shape never diverges.

| YAML section | Storage | Canonical JSON shape (camelCase) |
|--------------|---------|----------------------------------|
| `agent_name` | column `AgentName` (string) | — |
| `agent_type` | column `AgentType` (string) | — |
| `agent_version` | column `AgentVersion` (string) | — |
| `description` | column `Description` (string) | — |
| `visibility` | column `Visibility` (enum) | — |
| `chat_enabled` | column `ChatEnabled` (bool) | — |
| `produces_artifact` | column `ProducesArtifact` (bool, new in 7a) | — |
| `artifact_type` | column `ArtifactType` (string, new in 7a) | — |
| `model_config` | jsonb `ModelConfig` | `{ "provider": "...", "model": "...", "temperature": 0.2, "maxOutputTokens": 16000 }` |
| `tools` | jsonb `Tools` | `[ { "name": "file.read", "requiresApproval": false, "mcpServer": null } ]` |
| `scope` | jsonb `Scope` | `{ "fileScope": { "allowedPaths": [...], "deniedPaths": [...] }, "maxInputLength": 200000, "maxBudgetUsd": 2.0 }` |
| `constraints` | jsonb `Constraints` | `{ "maxToolCalls": 50, "timeoutSeconds": 600, "requireStructuredOutput": true }` |
| `interface` | jsonb `Interface` | `{ "inputSchema": {...}, "outputSchema": {...} }` |
| `thinking_budget` | jsonb `ThinkingBudget` | `{ "enabled": true, "maxTokens": 8000 }` |
| `system_prompt` | column `SystemPrompt` (text) | — |

Canonical JSON is produced via `WireJsonOptions.Default` (the same options the events outbox uses). A single static `AgentJsonShapes` class in Domain exposes records for each jsonb shape so the runtime parses them once:

```csharp
// src/AgenticWorkforce.Domain/Agents/AgentJsonShapes.cs
public sealed record AgentModelConfig(string Provider, string Model, double? Temperature, int? MaxOutputTokens);
public sealed record AgentToolBinding(string Name, bool RequiresApproval, string? McpServer);
public sealed record AgentScope(AgentFileScope FileScope, int? MaxInputLength, decimal? MaxBudgetUsd);
public sealed record AgentFileScope(string[] AllowedPaths, string[] DeniedPaths);
public sealed record AgentConstraints(int? MaxToolCalls, int? TimeoutSeconds, bool? RequireStructuredOutput);
public sealed record AgentInterface(JsonElement? InputSchema, JsonElement? OutputSchema);
public sealed record AgentThinkingBudget(bool Enabled, int? MaxTokens);
```

These live in **Domain** so the AgentFactory, PromptAssembler, and VerificationPipeline can all read the same types — no per-consumer parsing.

---

## 4. All 16 Agent Seed Files

### File: `src/AgenticWorkforce.Agents/Catalog/Seeds/`

| File | Agent Name | Category | Description |
|------|-----------|----------|-------------|
| `project.director.yaml` | `project.director` | project | Orchestrates project lifecycle, delegates to other agents |
| `project.planner.yaml` | `project.planner` | project | Creates task DAGs from objectives |
| `project.supervisor.yaml` | `project.supervisor` | project | Monitors execution, makes retry/escalation decisions |
| `research.strategist.yaml` | `research.strategist` | research | Plans research approach and decomposes queries |
| `research.searcher.yaml` | `research.searcher` | research | Executes web searches with domain expertise |
| `research.analyst.yaml` | `research.analyst` | research | Analyses and synthesises research findings |
| `research.synthesizer.yaml` | `research.synthesizer` | research | Produces final research reports/artifacts |
| `security.webapp.scanner.yaml` | `security.webapp.scanner` | security | OWASP Top 10 static analysis |
| `security.webapp.triage.yaml` | `security.webapp.triage` | security | Classifies and prioritises security findings |
| `security.webapp.reporter.yaml` | `security.webapp.reporter` | security | Generates security assessment reports |
| `software.code-analyst.yaml` | `software.code-analyst` | software | Analyses code quality, patterns, complexity |
| `software.architecture-reviewer.yaml` | `software.architecture-reviewer` | software | Reviews architecture decisions and dependencies |
| `software.quality-verifier.yaml` | `software.quality-verifier` | software | Verifies output quality against criteria |
| `system.summarizer.yaml` | `system.summarizer` | system | Compresses session history into rolling summaries |
| `system.verifier.yaml` | `system.verifier` | system | Independent output verification (adversarial) |
| `system.knowledge-officer.yaml` | `system.knowledge-officer` | system | Extracts learnings from task outputs |

### ApprovalRequired tool restriction (Phase 6 carry-over)

`ToolRegistry.Resolve` throws if any resolved tool has `RequiresApproval = true` until `ApprovalRequiredAIFunction` ships in Phase 8 (Phase 6 §4.3). For Phase 7 the following tools **must not** appear in any seeded manifest:

- `project.update_budget`
- `project.approve_tasks`
- `project.refine_plan`

These are added to manifests in Phase 8 when the workflow engine + approval wrapper exist. The architecture tests in 7b assert this constraint over the seeded YAMLs.

### Example YAML (security.webapp.scanner.yaml)

```yaml
agent_name: security.webapp.scanner
agent_type: security
agent_version: "1.0.0"
description: "Scans web application source code for OWASP Top 10 vulnerabilities using static analysis tools."
visibility: public
chat_enabled: false
produces_artifact: true
artifact_type: VulnerabilityReport

model_config:
  provider: foundry-anthropic
  model: claude-sonnet-4-6
  temperature: 0.2
  max_output_tokens: 16000

tools:
  - name: file.read
  - name: file.search
  - name: shell.execute
  - name: security.code.scan
  - name: security.deps.scan
  - name: security.secrets.scan
  - name: project.get_info
  - name: project.get_pcd

scope:
  file_scope:
    allowed_paths: ["**/*"]
    denied_paths: [".env", "**/*.key", "**/secrets/**"]
  max_input_length: 200000
  max_budget_usd: 2.00

constraints:
  max_tool_calls: 50
  timeout_seconds: 600
  require_structured_output: true

interface:
  input_schema:  { type: "object", properties: { target_path: { type: "string" } } }
  output_schema: { type: "object", properties: { findings: { type: "array" }, summary: { type: "string" } } }

thinking_budget:
  enabled: true
  max_tokens: 8000

system_prompt: |
  You are a security vulnerability scanner specialising in OWASP Top 10.

  ## Your Mission
  Scan the provided codebase for security vulnerabilities, prioritised by severity.

  ## Output Requirements
  - Return findings as structured JSON
  - Each finding must include: severity, category, file, line, description, recommendation
  - Include a summary with total counts by severity

  ## Constraints
  - Only report confirmed vulnerabilities with evidence
  - Do not report informational or style issues
  - If unsure about severity, escalate to human_decision
```

YAML deserialisation uses `YamlDotNet`'s default deserialiser configured with `IgnoreUnmatchedProperties = false` so a typo in a key name fails the build.

---

## 5. Platform Tools (In-Process)

### Marker interface (Phase 6 carry-over)

Every Platform tool implements `IPlatformTool` so the Phase 6 architecture test (`PlatformTools_DoNotDependOnHttpOrFileOrProcess`) actually constrains them. A Platform tool that needs network or filesystem at runtime is a category error — promote it to Sandbox.

### `Tools/Project/` — 15 tools (`ExecutionDomain.Platform`)

| File | Tool Name | Description | Phase-7 status |
|------|-----------|-------------|----------------|
| `GetProjectInfoTool.cs` | `project.get_info` | Project metadata, status, budget, team counts | Active |
| `GetProjectTeamTool.cs` | `project.get_team` | Agents assigned to project with roles | Active |
| `GetPcdTool.cs` | `project.get_pcd` | Returns full PCD JSON | Active |
| `GetHistoryTool.cs` | `project.get_history` | Recent project events (last N) | Active |
| `GetPlanTool.cs` | `project.get_plan` | Current task DAG with statuses | Active |
| `ListWorkflowsTool.cs` | `project.list_workflows` | Available workflow definitions | Active |
| `GetArtifactsTool.cs` | `project.get_artifacts` | Lists project artifacts | Active |
| `GetLearningsTool.cs` | `project.get_learnings` | Active learnings for context | Active |
| `RefinePlanTool.cs` | `project.refine_plan` | Updates task plan (adds/reorders tasks) | **Deferred to Phase 8** (RequiresApproval) |
| `ApproveTasksTool.cs` | `project.approve_tasks` | Bulk approve proposed tasks | **Deferred to Phase 8** (RequiresApproval) |
| `RunObjectiveTool.cs` | `project.run_objective` | Creates + dispatches an ad-hoc task | Active (no approval — agent-driven dispatch) |
| `RunWorkflowTool.cs` | `project.run_workflow` | Starts a workflow execution | **Deferred to Phase 8** (workflow engine) |
| `StartResearchTool.cs` | `project.start_research` | Creates research task with strategy | Active |
| `AddPrincipleTool.cs` | `project.add_principle` | Adds principle to PCD | Active (already gated by Principle 17 elsewhere) |
| `UpdateBudgetTool.cs` | `project.update_budget` | **Submits a human_decision task** to request budget extension (Principle 17 — never mutates budget directly) | **Deferred to Phase 8** (needs `IWorkflowEngine.SubmitHumanInputAsync`) |

Tools marked Active in Phase 7 are registered and used. Tools marked Deferred have their `.cs` files **omitted** until Phase 8 — they are not registered with `IToolRegistry` and their YAML references must be absent from Phase 7 manifests (see §4 restriction list).

### Tools/Supervisor/ — 2 tools (`ExecutionDomain.Platform`)

| File | Tool Name | Description |
|------|-----------|-------------|
| `GetRecentOutcomesTool.cs` | `project.get_recent_outcomes` | Last N task results for supervision |
| `GetPastDecisionsTool.cs` | `project.get_past_decisions` | Historical decisions for consistency |

### Tool implementation pattern

Tools capture project-scoped context at construction (from `AgentExecutionContext`), **not** from a model-supplied parameter. The LLM-facing signature must never accept `projectId` — prompt injection would otherwise allow cross-project reads.

```csharp
// src/AgenticWorkforce.Agents/Tools/Project/GetProjectInfoTool.cs
internal sealed class GetProjectInfoTool(
    Guid projectId,                            // captured from AgentExecutionContext at construction
    IProjectRepository projectRepo,
    ILogger<GetProjectInfoTool> logger) : IPlatformTool
{
    [Description("Get the current project's metadata: name, status, budget ceiling, and team composition.")]
    public async Task<string> GetInfoAsync(CancellationToken ct = default)
    {
        LogToolInvoked(logger, "project.get_info", projectId, null);

        var project = await projectRepo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var result = new
        {
            project.Id,
            project.Name,
            project.Objective,
            project.Status,
            project.BudgetCeilingUsd,
            MemberCount = project.Members.Count,
            AgentCount  = project.Agents.Count,
            project.CreatedAt
        };

        return JsonSerializer.Serialize(result, WireJsonOptions.Default);
    }

    private static readonly Action<ILogger, string, Guid, Exception?> LogToolInvoked =
        LoggerMessage.Define<string, Guid>(LogLevel.Debug,
            new EventId(1, nameof(LogToolInvoked)),
            "Tool {Tool} invoked for project {ProjectId}");
}
```

### Registration helper (`AIFunctionFactory`)

A single internal helper wraps each Platform tool's `[Description]`-annotated method into an `AITool` and registers it with `IToolRegistry`. This is the bridge the Phase 6 plan stopped short of:

```csharp
// src/AgenticWorkforce.Agents/Tools/PlatformToolFactory.cs
internal static class PlatformToolFactory
{
    public static AITool Wrap(Delegate method, string toolName) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = toolName });
}
```

Per-execution registration is done by the `AgentFactory` (Phase 6 step 3) using a per-tool factory delegate that captures `projectId` from `AgentExecutionContext` at construction:

```csharp
// Inside AgentFactory.CreateAsync, Step 3 (extended in 7c)
foreach (var binding in manifest)
{
    if (binding.Name == "project.get_info")
    {
        var tool = scope.ServiceProvider.GetRequiredService<GetProjectInfoToolFactory>()
            .Create(context.ProjectId);
        toolRegistry.RegisterPerExecution(binding, PlatformToolFactory.Wrap(tool.GetInfoAsync, binding.Name));
    }
    // ...
}
```

`IToolRegistry.RegisterPerExecution` is added in 7c as a per-execution overlay on top of the singleton registry. It does not mutate the singleton; it returns a scoped resolver.

---

## 6. Sandbox Tools (Fail-loud stubs)

Sandbox tools that don't have a real implementation yet **must throw**, not return placeholder strings. A "not available" string fed back into the model is silent failure (Principle 8). A thrown `SandboxUnavailableException` surfaces as a tool error in the MAF tool loop, and the agent either escalates or fails — both visible.

```csharp
// src/AgenticWorkforce.Domain/Exceptions/AppException.cs (new exception)
public class SandboxUnavailableException(string toolName)
    : AppException(ErrorCodes.SandboxUnavailable,
        $"Sandbox tool '{toolName}' is not yet available — ACA Dynamic Sessions wiring lands in Phase 11.", 503);
```

### `Tools/Common/` — 6 tools (`ExecutionDomain.Sandbox`)

| File | Tool Name |
|------|-----------|
| `FileReadTool.cs` | `file.read` |
| `FileWriteTool.cs` | `file.write` |
| `FileSearchTool.cs` | `file.search` |
| `ShellExecuteTool.cs` | `shell.execute` |
| `WebSearchTool.cs` | `web.search` |
| `WebFetchTool.cs` | `web.fetch` |

### `Tools/Security/` — 4 tools (`ExecutionDomain.Sandbox`)

| File | Tool Name |
|------|-----------|
| `CodeScanTool.cs` | `security.code.scan` |
| `DependencyScanTool.cs` | `security.deps.scan` |
| `SecretScanTool.cs` | `security.secrets.scan` |
| `VulnLookupTool.cs` | `security.vuln.lookup` |

### `Tools/Research/` — 3 tools (`ExecutionDomain.Sandbox`)

| File | Tool Name |
|------|-----------|
| `DeepSearchTool.cs` | `research.web.search` |
| `ContentExtractTool.cs` | `research.extract` |
| `SourceEvaluateTool.cs` | `research.source.evaluate` |

### `Tools/Software/` — 3 tools (`ExecutionDomain.Sandbox`)

| File | Tool Name |
|------|-----------|
| `CodeAnalysisTool.cs` | `software.code.analyze` |
| `ArchitectureMapTool.cs` | `software.arch.map` |
| `TestRunTool.cs` | `software.test.run` |

### Sandbox stub pattern

```csharp
// src/AgenticWorkforce.Agents/Tools/Common/WebSearchTool.cs
internal sealed class WebSearchTool
{
    public const string Name = "web.search";

    [Description("Search the web for relevant pages. Returns the top results with title, URL, and snippet.")]
    public Task<string> SearchAsync(
        [Description("Search query")] string query,
        CancellationToken ct = default)
        => throw new SandboxUnavailableException(Name);
}
```

All 18 sandbox stubs follow this pattern. Phase 11 replaces the throw with a real ACA Dynamic Sessions invocation.

---

## 7. Verification Pipeline

### Architecture (3-tier)

```
Agent output
    │
    ▼
Tier 1: SchemaVerifier — JSON schema validation (fast, deterministic)
    │ pass/fail
    ▼
Tier 2: RuleVerifier — Business rule checks (deterministic)
    │ pass/fail
    ▼
Tier 3: AgentVerifier — Independent agent review (system.verifier) — only when ProducesArtifact
    │ pass/fail + feedback
    ▼
Result: Passed | Failed(tier, reason, feedback)
```

### Files

```
src/AgenticWorkforce.Agents/Verification/IVerifier.cs
src/AgenticWorkforce.Agents/Verification/VerificationPipeline.cs
src/AgenticWorkforce.Agents/Verification/SchemaVerifier.cs
src/AgenticWorkforce.Agents/Verification/RuleVerifier.cs
src/AgenticWorkforce.Agents/Verification/AgentVerifier.cs
src/AgenticWorkforce.Agents/Verification/VerificationResult.cs
```

### IVerifier

```csharp
public interface IVerifier
{
    Task<VerificationResult> VerifyAsync(
        AgenticTask task, string output, AgentCatalog agent, CancellationToken ct);
}

public enum FailureTier { Tier1Structural, Tier2Rules, Tier3Agent }

public sealed record VerificationResult(
    bool Passed,
    FailureTier? FailedAt,
    string? Reason,
    string? Feedback)
{
    public static readonly VerificationResult Pass = new(true, null, null, null);
}
```

### VerificationPipeline (with recursion guard)

```csharp
internal sealed class VerificationPipeline(
    SchemaVerifier schema,
    RuleVerifier rules,
    AgentVerifier agentVerifier,
    ILogger<VerificationPipeline> logger) : IVerifier
{
    public const string SystemVerifierAgentName = "system.verifier";

    public async Task<VerificationResult> VerifyAsync(
        AgenticTask task, string output, AgentCatalog agent, CancellationToken ct)
    {
        var schemaResult = schema.Verify(output, agent);
        if (!schemaResult.Passed) return schemaResult;

        var ruleResult = rules.Verify(task, output);
        if (!ruleResult.Passed) return ruleResult;

        // Recursion guard: never run Tier 3 against the verifier's own output. Otherwise
        // verifying system.verifier would recursively invoke system.verifier and the
        // pipeline would diverge.
        if (agent.ProducesArtifact
            && !string.Equals(agent.AgentName, SystemVerifierAgentName, StringComparison.Ordinal))
        {
            return await agentVerifier.VerifyAsync(task, output, agent, ct);
        }

        return VerificationResult.Pass;
    }
}
```

### SchemaVerifier

Validates `output` against `AgentCatalog.Interface.outputSchema`. Phase 7 ships shape-only validation (required-properties present, types correct). Full JSON-Schema draft-2020 support is Phase 11 polish.

```csharp
internal sealed class SchemaVerifier
{
    public VerificationResult Verify(string output, AgentCatalog agent)
    {
        if (agent.Interface is null) return VerificationResult.Pass;

        var iface = JsonSerializer.Deserialize<AgentInterface>(agent.Interface, WireJsonOptions.Default);
        if (iface?.OutputSchema is null) return VerificationResult.Pass;

        JsonNode? node;
        try { node = JsonNode.Parse(output); }
        catch (JsonException ex)
        {
            return new VerificationResult(false, FailureTier.Tier1Structural,
                $"JSON parse error: {ex.Message}", null);
        }

        return node is null
            ? new VerificationResult(false, FailureTier.Tier1Structural, "Output is null JSON", null)
            : ValidateRequiredProperties(node, iface.OutputSchema.Value);
    }
}
```

### AgentVerifier (Tier 3) — real Phase-6 contract

`AgentVerifier` calls `IAgentRuntime.ExecuteAsync(AgentExecutionRequest)` — the actual Phase 6 contract. No invented overloads, no constructed `ProjectContext`:

```csharp
internal sealed class AgentVerifier(IAgentRuntime runtime)
{
    public async Task<VerificationResult> VerifyAsync(
        AgenticTask task, string output, AgentCatalog agent, CancellationToken ct)
    {
        var objective = $"""
            Verify this agent output meets quality requirements.

            Task objective: {task.Objective}
            Agent: {agent.AgentName}
            Output to verify:
            {output[..Math.Min(output.Length, 10_000)]}

            Respond with JSON: {{ "passed": true/false, "reason": "...", "feedback": "..." }}
            """;

        var request = new AgentExecutionRequest(
            ProjectId:  task.ProjectId,
            TaskId:     task.Id,
            AgentName:  VerificationPipeline.SystemVerifierAgentName,
            Objective:  objective);

        var result = await runtime.ExecuteAsync(request, ct);
        return ParseVerifierResponse(result.Output);
    }
}
```

### Budget cost of Tier 3

Every Tier 3 verification is an LLM call. For Phase 7 verification, only artefact-producing agents trigger Tier 3, and the verifier uses `claude-haiku-4-5` (configured per `system.verifier`'s `model_config` in its YAML) — roughly 10× cheaper than Sonnet. The cost is recorded by `CostTrackingChatClient` like any other call; budget caps still apply.

---

## 8. MCP Tool Resolution (Stub)

### Files

```
src/AgenticWorkforce.Agents/Tools/Mcp/IMcpToolResolver.cs
src/AgenticWorkforce.Agents/Tools/Mcp/McpToolResolver.cs
```

```csharp
public interface IMcpToolResolver
{
    AITool Resolve(ToolBinding binding);
}

internal sealed class McpToolResolver(ILogger<McpToolResolver> logger) : IMcpToolResolver
{
    public AITool Resolve(ToolBinding binding)
    {
        LogMcpUnavailable(logger, binding.Name, binding.McpServer ?? "<unset>", null);
        throw new InvalidStateException(
            $"MCP tool '{binding.Name}' requires server '{binding.McpServer}' which is not yet configured.");
    }

    private static readonly Action<ILogger, string, string, Exception?> LogMcpUnavailable =
        LoggerMessage.Define<string, string>(LogLevel.Warning,
            new EventId(1, nameof(LogMcpUnavailable)),
            "MCP tool {ToolName} from server {McpServer} not yet available");
}
```

---

## 9. Tests

Tests for verification + tool registry live in **`AgenticWorkforce.Agents.Tests.Unit`** (not Domain). Tests that exercise EF Core / Testcontainers live in **`AgenticWorkforce.Api.Tests.Integration`** (consumes the existing `ApiWebApplicationFactory` fixture).

### Unit tests (`AgenticWorkforce.Agents.Tests.Unit`)

- `Verification/SchemaVerifierTests.cs` — well-formed and malformed JSON; missing required property; null Interface short-circuits to Pass.
- `Verification/RuleVerifierTests.cs` — passes / fails per business-rule fixture.
- `Verification/VerificationPipelineTests.cs` — Tier 1 short-circuit, Tier 2 short-circuit, Tier 3 invoked only when `ProducesArtifact`, recursion guard on `system.verifier`.
- `Tools/PlatformToolFactoryTests.cs` — `AIFunctionFactory.Create` produces a callable `AITool` with the bound `Name`.
- `Tools/SandboxToolStubTests.cs` — every sandbox stub throws `SandboxUnavailableException`.

### Integration tests (`AgenticWorkforce.Api.Tests.Integration`)

- `Services/AgentSeedServiceTests.cs` — first start seeds 16 agents; second start is a no-op; bumping `agent_version` updates the row and appends a `PromptVersion`; equal-version YAML edit is ignored; malformed `agent_version` crashes seed.

### Architecture tests (extend `tests/AgenticWorkforce.Architecture.Tests`)

- `SeededYamlTests.cs` — every embedded YAML resource parses; no YAML references a Phase-8-deferred tool name; no YAML sets `requires_approval: true`.
- Existing `ModuleBoundaryTests.PlatformTools_DoNotDependOnHttpOrFileOrProcess` will now have non-zero implementers to cover.

---

## 10. Package Additions

```xml
<PackageVersion Include="YamlDotNet" Version="16.3.0" />
```

YamlDotNet deserialiser configured with `IgnoreUnmatchedProperties = false` and the default scalar resolver (no tag-driven type instantiation).

---

## File Summary

### Files to CREATE (~70 files across 7a-7e)

```
# 7a (~6 files)
src/AgenticWorkforce.Infrastructure/Migrations/<timestamp>_Phase7AgentCatalogProducesArtifact.cs
src/AgenticWorkforce.Infrastructure/Services/AgentSeedService.cs
src/AgenticWorkforce.Infrastructure/Services/EmbeddedYamlAgentSeedSource.cs
src/AgenticWorkforce.Infrastructure/Services/AgentSeedMapper.cs
src/AgenticWorkforce.Infrastructure/Services/AgentSemver.cs
src/AgenticWorkforce.Agents/AgentsAssemblyMarker.cs                    (public type token)
src/AgenticWorkforce.Agents/Catalog/Seeds/system.verifier.yaml         (1 proof YAML)
tests/AgenticWorkforce.Api.Tests.Integration/Services/AgentSeedServiceTests.cs

# 7b (~17 files)
src/AgenticWorkforce.Agents/Catalog/Seeds/*.yaml                       (15 remaining YAMLs)
src/AgenticWorkforce.Domain/Agents/AgentJsonShapes.cs                  (canonical JSON shape records)
tests/AgenticWorkforce.Architecture.Tests/SeededYamlTests.cs

# 7c (~17 files)
src/AgenticWorkforce.Agents/Tools/PlatformToolFactory.cs
src/AgenticWorkforce.Agents/Tools/Project/ (12 .cs files — 3 deferred per §5 table)
src/AgenticWorkforce.Agents/Tools/Project/PlatformToolRegistrations.cs (per-execution registration helper)
tests/AgenticWorkforce.Agents.Tests.Unit/Tools/PlatformToolFactoryTests.cs

# 7d (~20 files)
src/AgenticWorkforce.Agents/Tools/Supervisor/ (2 .cs files)
src/AgenticWorkforce.Agents/Tools/Common/ (6 .cs files — throwing stubs)
src/AgenticWorkforce.Agents/Tools/Security/ (4 .cs files — throwing stubs)
src/AgenticWorkforce.Agents/Tools/Research/ (3 .cs files — throwing stubs)
src/AgenticWorkforce.Agents/Tools/Software/ (3 .cs files — throwing stubs)
src/AgenticWorkforce.Domain/Exceptions/AppException.cs                 (add SandboxUnavailableException + ErrorCodes.SandboxUnavailable)
tests/AgenticWorkforce.Agents.Tests.Unit/Tools/SandboxToolStubTests.cs

# 7e (~10 files)
src/AgenticWorkforce.Agents/Tools/Mcp/IMcpToolResolver.cs
src/AgenticWorkforce.Agents/Tools/Mcp/McpToolResolver.cs
src/AgenticWorkforce.Agents/Verification/IVerifier.cs
src/AgenticWorkforce.Agents/Verification/VerificationPipeline.cs
src/AgenticWorkforce.Agents/Verification/VerificationResult.cs
src/AgenticWorkforce.Agents/Verification/SchemaVerifier.cs
src/AgenticWorkforce.Agents/Verification/RuleVerifier.cs
src/AgenticWorkforce.Agents/Verification/AgentVerifier.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Verification/SchemaVerifierTests.cs
tests/AgenticWorkforce.Agents.Tests.Unit/Verification/VerificationPipelineTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Agents/DependencyInjection.cs       — Register verification pipeline + per-execution tool registry helper
src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj — Embed Catalog/Seeds/*.yaml
src/AgenticWorkforce.Agents/Runtime/AgentFactory.cs      — Step 3 now resolves manifest tools via PlatformToolFactory + McpToolResolver
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs — Register AgentSeedService, IAgentSeedSource, AgentSeedMapper, YamlDotNet deserialiser
src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj — Add YamlDotNet
src/AgenticWorkforce.Worker/Program.cs                   — No code changes needed; AddInfrastructure pulls in the seeder
Directory.Packages.props                                  — Add YamlDotNet
src/AgenticWorkforce.Domain/Entities/AgentCatalog.cs     — Add ProducesArtifact, ArtifactType
src/AgenticWorkforce.Domain/Errors/ErrorCodes.cs         — Add SandboxUnavailable
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0.
2. `dotnet test AgenticWorkforce.slnx` exits 0. New tests include the unit/integration/architecture suites listed in §9.
3. On Worker startup, `agent_catalogs` table has 16 rows with correct `agent_name` values and a non-null `agent_version` per row.
4. Re-running the Worker is a no-op (idempotent); no new `PromptVersion` rows when YAMLs are unchanged.
5. Bumping `agent_version` in a YAML and restarting the Worker:
   - updates the `AgentCatalog` row
   - appends a `PromptVersion` row if `system_prompt` changed
6. Every Platform tool in `AgenticWorkforce.Agents/Tools/Project/` and `Tools/Supervisor/` implements `IPlatformTool` and passes the Phase 6 architecture test.
7. Every Sandbox tool throws `SandboxUnavailableException` when invoked (no silent placeholder strings).
8. `VerificationPipeline.VerifyAsync` against `system.verifier`'s own output does **not** recurse (recursion guard test).
9. `McpToolResolver.Resolve` throws `InvalidStateException` for any binding with an `McpServer` set.
10. No seeded YAML references a Phase-8-deferred tool (`project.update_budget`, `project.approve_tasks`, `project.refine_plan`, `project.run_workflow`). Architecture test asserts.
11. CQI score does not regress below the previous phase's baseline.

---

## Goal Command

```
/goal Phase 7 lands as 7a-7e. 7a: Infrastructure-side AgentSeedService (consumes IAgentCatalogRepository; runs before LlmCallDrainService), AgentCatalog gains ProducesArtifact/ArtifactType columns via migration, AgentSemver parses Major.Minor.Patch strictly, 1 proof YAML (system.verifier) seeds end-to-end. 7b: remaining 15 YAMLs + AgentJsonShapes in Domain so seeder writes and runtime reads share one canonical JSON shape per jsonb column; SeededYamlTests architecture test asserts no Phase-8-deferred tools and no requires_approval flags. 7c: 12 Platform tools under Tools/Project/ (3 deferred to Phase 8 per §5 table), all implement IPlatformTool, projectId captured from AgentExecutionContext at construction (never model-supplied), PlatformToolFactory wraps [Description] methods into AITools. 7d: 2 supervisor Platform tools + 18 sandbox stubs that throw SandboxUnavailableException (no placeholder strings). 7e: 3-tier VerificationPipeline with recursion guard for system.verifier; AgentVerifier uses real IAgentRuntime.ExecuteAsync(AgentExecutionRequest); MCP resolver throws InvalidStateException. Verify: dotnet build exits 0, dotnet test exits 0, Worker startup seeds 16 agents, CQI does not regress. Stop after 40 turns per sub-phase.
```
