# Phase 7: Agent Catalog & Tools

**Status:** Not Started
**Depends On:** Phase 6 (Agent Runtime)
**Verification:** Agent catalog seeded to DB on startup, tools resolve from manifest, verification pipeline runs

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

---

## 1. Agent Seed Strategy

### How it works (from 007-agent-implementation.md §7)

On startup, `AgentSeedService` (hosted service) reads YAML files from embedded resources and upserts them into the `AgentCatalog` table. This is idempotent — existing entries are updated only if the YAML version is newer.

```csharp
internal sealed class AgentSeedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var yamls = LoadEmbeddedYaml();
        foreach (var (name, definition) in yamls)
        {
            var existing = await db.AgentCatalogs
                .FirstOrDefaultAsync(a => a.AgentName == name, ct);

            if (existing == null)
            {
                db.AgentCatalogs.Add(MapToEntity(definition));
                logger.LogInformation("Seeded agent {AgentName}", name);
            }
            else if (IsNewer(definition, existing))
            {
                UpdateEntity(existing, definition);
                logger.LogInformation("Updated agent {AgentName} to v{Version}",
                    name, definition.Version);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

### YAML Schema

```yaml
# Example: security.webapp.scanner.yaml
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
  input_schema: { type: "object", properties: { target_path: { type: "string" } } }
  output_schema: { type: "object", properties: { findings: { type: "array" }, summary: { type: "string" } } }

thinking_budget:
  enabled: true
  max_tokens: 8000

system_prompt: |
  You are a security vulnerability scanner specializing in OWASP Top 10.

  ## Your Mission
  Scan the provided codebase for security vulnerabilities, prioritized by severity.

  ## Output Requirements
  - Return findings as structured JSON
  - Each finding must include: severity, category, file, line, description, recommendation
  - Include a summary with total counts by severity

  ## Constraints
  - Only report confirmed vulnerabilities with evidence
  - Do not report informational or style issues
  - If unsure about severity, escalate to human_decision
```

---

## 2. All 16 Agent Seed Files

### File: `src/AgenticWorkforce.Agents/Catalog/Seeds/`

| File | Agent Name | Category | Description |
|------|-----------|----------|-------------|
| `project.director.yaml` | `project.director` | project | Orchestrates project lifecycle, delegates to other agents |
| `project.planner.yaml` | `project.planner` | project | Creates task DAGs from objectives |
| `project.supervisor.yaml` | `project.supervisor` | project | Monitors execution, makes retry/escalation decisions |
| `research.strategist.yaml` | `research.strategist` | research | Plans research approach and decomposes queries |
| `research.searcher.yaml` | `research.searcher` | research | Executes web searches with domain expertise |
| `research.analyst.yaml` | `research.analyst` | research | Analyzes and synthesizes research findings |
| `research.synthesizer.yaml` | `research.synthesizer` | research | Produces final research reports/artifacts |
| `security.webapp.scanner.yaml` | `security.webapp.scanner` | security | OWASP Top 10 static analysis |
| `security.webapp.triage.yaml` | `security.webapp.triage` | security | Classifies and prioritizes security findings |
| `security.webapp.reporter.yaml` | `security.webapp.reporter` | security | Generates security assessment reports |
| `software.code-analyst.yaml` | `software.code-analyst` | software | Analyzes code quality, patterns, complexity |
| `software.architecture-reviewer.yaml` | `software.architecture-reviewer` | software | Reviews architecture decisions and dependencies |
| `software.quality-verifier.yaml` | `software.quality-verifier` | software | Verifies output quality against criteria |
| `system.summarizer.yaml` | `system.summarizer` | system | Compresses session history into rolling summaries |
| `system.verifier.yaml` | `system.verifier` | system | Independent output verification (adversarial) |
| `system.knowledge-officer.yaml` | `system.knowledge-officer` | system | Extracts learnings from task outputs |

Each YAML file defines: agent_name, agent_type, agent_version, description, model_config, tools, scope, constraints, interface, thinking_budget, system_prompt, visibility, chat_enabled, produces_artifact.

---

## 3. Platform Tools (In-Process)

### `Tools/Project/` — 15 tools (ExecutionDomain.Platform)

These run in-process, querying our own database. No external calls.

| File | Tool Name | Description |
|------|-----------|-------------|
| `GetProjectInfoTool.cs` | `project.get_info` | Returns project metadata, status, budget, team |
| `GetProjectTeamTool.cs` | `project.get_team` | Lists agents assigned to project with roles |
| `GetPcdTool.cs` | `project.get_pcd` | Returns full PCD JSON |
| `GetHistoryTool.cs` | `project.get_history` | Recent project events (last N) |
| `GetPlanTool.cs` | `project.get_plan` | Current task DAG with statuses |
| `ListWorkflowsTool.cs` | `project.list_workflows` | Available workflow definitions |
| `GetArtifactsTool.cs` | `project.get_artifacts` | Lists project artifacts |
| `GetLearningsTool.cs` | `project.get_learnings` | Active learnings for context |
| `RefinePlanTool.cs` | `project.refine_plan` | Updates task plan (adds/reorders tasks) |
| `ApproveTasksTool.cs` | `project.approve_tasks` | Bulk approve proposed tasks |
| `RunObjectiveTool.cs` | `project.run_objective` | Creates + dispatches an ad-hoc task |
| `RunWorkflowTool.cs` | `project.run_workflow` | Starts a workflow execution |
| `StartResearchTool.cs` | `project.start_research` | Creates research task with strategy |
| `AddPrincipleTool.cs` | `project.add_principle` | Adds principle to PCD |
| `UpdateBudgetTool.cs` | `project.update_budget` | Requests budget extension |

### `Tools/Supervisor/` — 2 tools (ExecutionDomain.Platform)

| File | Tool Name | Description |
|------|-----------|-------------|
| `GetRecentOutcomesTool.cs` | `project.get_recent_outcomes` | Last N task results for supervision |
| `GetPastDecisionsTool.cs` | `project.get_past_decisions` | Historical decisions for consistency |

### Tool Implementation Pattern

```csharp
internal sealed class GetProjectInfoTool(
    IProjectRepository projectRepo,
    ILogger<GetProjectInfoTool> logger)
{
    [Description("Get project metadata including name, status, budget, and team composition")]
    public async Task<string> GetInfoAsync(
        [Description("The project ID")] Guid projectId,
        CancellationToken ct = default)
    {
        logger.LogDebug("Tool project.get_info called for {ProjectId}", projectId);

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
            AgentCount = project.Agents.Count,
            project.CreatedAt
        };

        return JsonSerializer.Serialize(result);
    }
}
```

---

## 4. Sandbox Tools (Stubs)

### `Tools/Common/` — 6 tools (ExecutionDomain.Sandbox)

These will delegate to ACA Dynamic Sessions in production. For Phase 7, they are stubs that return placeholder responses:

| File | Tool Name | Stub Behaviour |
|------|-----------|----------------|
| `FileReadTool.cs` | `file.read` | Returns "File read not available without sandbox" |
| `FileWriteTool.cs` | `file.write` | Returns "File write not available without sandbox" |
| `FileSearchTool.cs` | `file.search` | Returns empty results |
| `ShellExecuteTool.cs` | `shell.execute` | Returns "Shell execution not available without sandbox" |
| `WebSearchTool.cs` | `web.search` | Returns "Web search not available without sandbox" |
| `WebFetchTool.cs` | `web.fetch` | Returns "Web fetch not available without sandbox" |

### `Tools/Security/` — 4 tools (ExecutionDomain.Sandbox)

| File | Tool Name | Stub Behaviour |
|------|-----------|----------------|
| `CodeScanTool.cs` | `security.code.scan` | Returns empty findings |
| `DependencyScanTool.cs` | `security.deps.scan` | Returns empty findings |
| `SecretScanTool.cs` | `security.secrets.scan` | Returns empty findings |
| `VulnLookupTool.cs` | `security.vuln.lookup` | Returns "not available" |

### `Tools/Research/` — 3 tools (ExecutionDomain.Sandbox)

| File | Tool Name | Stub Behaviour |
|------|-----------|----------------|
| `DeepSearchTool.cs` | `research.web.search` | Returns empty results |
| `ContentExtractTool.cs` | `research.extract` | Returns empty content |
| `SourceEvaluateTool.cs` | `research.source.evaluate` | Returns neutral score |

### `Tools/Software/` — 3 tools (ExecutionDomain.Sandbox)

| File | Tool Name | Stub Behaviour |
|------|-----------|----------------|
| `CodeAnalysisTool.cs` | `software.code.analyze` | Returns empty analysis |
| `ArchitectureMapTool.cs` | `software.arch.map` | Returns empty map |
| `TestRunTool.cs` | `software.test.run` | Returns "not available" |

---

## 5. Verification Pipeline

### Architecture (3-tier from 007-agent-implementation.md)

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
Tier 3: AgentVerifier — Independent agent review (system.verifier)
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

public record VerificationResult(
    bool Passed,
    FailureTier? FailedAt,
    string? Reason,
    string? Feedback);
```

### VerificationPipeline

```csharp
internal sealed class VerificationPipeline(
    SchemaVerifier schema,
    RuleVerifier rules,
    AgentVerifier agentVerifier,
    ILogger<VerificationPipeline> logger) : IVerifier
{
    public async Task<VerificationResult> VerifyAsync(
        AgenticTask task, string output, AgentCatalog agent, CancellationToken ct)
    {
        // Tier 1: Schema
        var schemaResult = schema.Verify(output, agent);
        if (!schemaResult.Passed)
        {
            logger.LogWarning("Tier 1 schema verification failed for task {TaskId}", task.Id);
            return schemaResult;
        }

        // Tier 2: Rules
        var ruleResult = rules.Verify(task, output);
        if (!ruleResult.Passed)
        {
            logger.LogWarning("Tier 2 rule verification failed for task {TaskId}", task.Id);
            return ruleResult;
        }

        // Tier 3: Agent (only if agent has require_structured_output or produces_artifact)
        if (agent.ProducesArtifact)
        {
            var agentResult = await agentVerifier.VerifyAsync(task, output, agent, ct);
            if (!agentResult.Passed)
            {
                logger.LogWarning("Tier 3 agent verification failed for task {TaskId}", task.Id);
                return agentResult;
            }
        }

        return new VerificationResult(true, null, null, null);
    }
}
```

### SchemaVerifier

Validates output against `AgentCatalog.Interface.output_schema` using `System.Text.Json.Nodes`:

```csharp
internal sealed class SchemaVerifier
{
    public VerificationResult Verify(string output, AgentCatalog agent)
    {
        if (agent.Interface is null) return VerificationResult.Pass;

        var iface = JsonSerializer.Deserialize<AgentInterface>(agent.Interface);
        if (iface?.OutputSchema is null) return VerificationResult.Pass;

        // Validate output parses as valid JSON matching schema
        try
        {
            var node = JsonNode.Parse(output);
            if (node is null)
                return new VerificationResult(false, FailureTier.Tier1Structural,
                    "Output is not valid JSON", null);

            // Check required properties exist
            // (Full JSON Schema validation via a library would be Phase 11 polish)
            return ValidateRequiredProperties(node, iface.OutputSchema);
        }
        catch (JsonException ex)
        {
            return new VerificationResult(false, FailureTier.Tier1Structural,
                $"JSON parse error: {ex.Message}", null);
        }
    }
}
```

### AgentVerifier (Tier 3)

Runs `system.verifier` agent against the output:

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
            {output[..Math.Min(output.Length, 10000)]}

            Respond with JSON: {{ "passed": true/false, "reason": "...", "feedback": "..." }}
            """;

        var context = new ProjectContext(task.ProjectId, null, null, null);
        var result = await runtime.RunAsync("system.verifier", objective, context, ct: ct);

        // Parse verifier response
        return ParseVerifierResponse(result.Output);
    }
}
```

---

## 6. MCP Tool Resolution (Stub)

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

// Stub — real MCP client integration deferred until container sandbox is available
internal sealed class McpToolResolver(ILogger<McpToolResolver> logger) : IMcpToolResolver
{
    public AITool Resolve(ToolBinding binding)
    {
        logger.LogWarning("MCP tool {ToolName} from server {McpServer} not yet available",
            binding.Name, binding.McpServer);
        throw new InvalidOperationException(
            $"MCP tool '{binding.Name}' requires server '{binding.McpServer}' which is not yet configured.");
    }
}
```

---

## File Summary

### Files to CREATE (~50 files)

```
src/AgenticWorkforce.Agents/Catalog/AgentSeedService.cs
src/AgenticWorkforce.Agents/Catalog/Seeds/ (16 YAML files)
src/AgenticWorkforce.Agents/Tools/Project/ (15 .cs files)
src/AgenticWorkforce.Agents/Tools/Supervisor/ (2 .cs files)
src/AgenticWorkforce.Agents/Tools/Common/ (6 .cs files — stubs)
src/AgenticWorkforce.Agents/Tools/Security/ (4 .cs files — stubs)
src/AgenticWorkforce.Agents/Tools/Research/ (3 .cs files — stubs)
src/AgenticWorkforce.Agents/Tools/Software/ (3 .cs files — stubs)
src/AgenticWorkforce.Agents/Tools/Mcp/IMcpToolResolver.cs
src/AgenticWorkforce.Agents/Tools/Mcp/McpToolResolver.cs
src/AgenticWorkforce.Agents/Verification/IVerifier.cs
src/AgenticWorkforce.Agents/Verification/VerificationPipeline.cs
src/AgenticWorkforce.Agents/Verification/VerificationResult.cs
src/AgenticWorkforce.Agents/Verification/SchemaVerifier.cs
src/AgenticWorkforce.Agents/Verification/RuleVerifier.cs
src/AgenticWorkforce.Agents/Verification/AgentVerifier.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Agents/VerificationPipelineTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Agents/SchemaVerifierTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Agents/AgentSeedServiceTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Agents/DependencyInjection.cs — Register seed service + verification
src/AgenticWorkforce.Agents/AgenticWorkforce.Agents.csproj — Add YamlDotNet, embed YAML resources
src/AgenticWorkforce.Worker/Program.cs — AgentSeedService registered as hosted service
Directory.Packages.props — Add YamlDotNet
```

### Package Additions

```xml
<PackageVersion Include="YamlDotNet" Version="16.3.0" />
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test` — all tests pass:
   - `AgentSeedServiceTests`: seeds 16 agents on startup, idempotent on re-run
   - `VerificationPipelineTests`: Tier 1 rejects malformed JSON, Tier 2 checks rules
   - `SchemaVerifierTests`: validates output against agent interface schema
3. On Worker startup, `AgentCatalogs` table has 16 rows with correct names
4. `ToolRegistry` resolves all tools referenced in agent manifests without error
5. Platform tools (project.*) return real data from database
6. Sandbox tools return stub responses (not throw)
7. `system.verifier` agent is seeded and can be resolved by `AgentCatalogResolver`
8. YAML files are embedded resources (not filesystem-dependent)

---

## Goal Command

```
/goal Agent catalog and tools complete: 16 agent seed YAMLs in embedded resources, AgentSeedService upserts on startup. 15 platform tools (project.*) return real DB data. 18 sandbox tool stubs return placeholder responses. 3-tier VerificationPipeline: SchemaVerifier validates JSON structure, RuleVerifier checks business rules, AgentVerifier calls system.verifier. MCP resolver stub logs warning. Verify: dotnet build exits 0, dotnet test exits 0, Worker startup seeds 16 agents to DB. Stop after 40 turns.
```
