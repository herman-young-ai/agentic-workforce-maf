# Phase 1: Domain Alignment

**Status:** Not Started
**Depends On:** None
**Verification:** `dotnet build AgenticWorkforce.slnx` exits 0, all entities match `docs/002-architecture/003-database-schema.md`

---

## Objective

Align the domain entities, enums, and interfaces in `src/AgenticWorkforce.Domain/` to exactly match the architecture specification in `docs/002-architecture/003-database-schema.md`. The current scaffold has simplified entities that diverge significantly from the spec. This phase corrects all mismatches so subsequent phases build on an accurate foundation.

---

## Current State vs Target State

### Entities that need REWRITING (significant divergence)

| Entity | Current File | Issue |
|--------|-------------|-------|
| `AgenticTask` | `Entities/AgenticTask.cs` | Missing: Objective, AgentName, Source, OutputSummary, CostUsd, DurationSeconds, RetryCount, MaxRetries, CreatedById, FormatVersion, WorkflowRunId. Has: Title/Description (not in spec), AssignedAgentId FK (spec uses AgentName string). |
| `TaskAttempt` | `Entities/AgenticTask.cs` | Missing: FailureTier, FailureReason, FeedbackProvided. Has: TokensUsed (spec uses InputTokens + OutputTokens separately), Error (spec uses FailureReason). |
| `Project` | `Entities/Project.cs` | Missing: Objective, Brief, BudgetCeilingUsd, Jurisdiction, TemplateName, Tier, IsActive. Has: TenantId (keep), Priority (keep). |
| `ProjectLearning` | `Entities/ProjectLearning.cs` | Missing: Title, Body, Recommendation, Confidence, OccurrenceCount, Evidence, AgentNames[], DomainTags[], SupersededById, ContradictsId, PlatformPromoted, PromotedBy, PromotedAt, FormatVersion. |
| `Session` | `Entities/Session.cs` | Missing: Status enum, AgentName, Goal, RollingSummary, RollingSummaryAnchor, RollingSummaryVersion, token counts, cost fields, LastActivityAt, ExpiresAt, Channels collection. |
| `SessionMessage` | `Entities/Session.cs` | Missing: SenderId, Model, InputTokens, OutputTokens, CostUsd, Thinking, ToolName, ToolCallId, Status. |
| `WorkflowDefinition` | `Entities/WorkflowDefinition.cs` | Spec uses JSON nodes/edges on the entity (not separate WorkflowNode/WorkflowEdge entities). Missing: Version, Enabled, CanvasState, DesignedBy, DesignedByAgent, LockedAt, FormatVersion. |
| `AgentCatalog` | `Entities/AgentCatalog.cs` | Spec is fundamentally different structure. Uses: AgentName (unique), AgentType, ModelConfig (jsonb), Tools (jsonb), Scope (jsonb), Interface (jsonb), Constraints (jsonb), Keywords[], ThinkingBudget, Visibility, Engine, MaxInputLength, MaxBudgetUsd, ProducesArtifact, ArtifactType. |
| `PlatformUser` → `User` | `Entities/PlatformUser.cs` | Rename to `User`. Missing: HashedPassword, SystemRole, IsServiceAccount, LastLoginAt, Sessions nav. |
| `ApiKey` | `Entities/PlatformUser.cs` | Missing: Scopes (jsonb), RevokedAt. Different: IssuedTo → UserId, Role → removed (scopes instead). |
| `LlmCall` | `Entities/LlmCall.cs` | Missing: AgentRole, CacheReadTokens, CacheCreationTokens, RequestId, ToolCount. Different: InputTokens (int→long), OutputTokens (int→long). |
| `ProjectEvent` | `Entities/ProjectEvent.cs` | Missing: SessionId, Source field. Remove: AgentName (use Source), UserId (use Source). |
| `ProjectDocument` | `Entities/ProjectDocument.cs` | Missing: ExtractedText, ExtractedTextUrl, PageCount, ExtractionStatus, ExtractionError, DocumentType, Description, Tags[], EmbeddingsGenerated, UploadedById FK. Remove: unused System.Numerics import. |
| `DocumentChunk` | `Entities/ProjectDocument.cs` | Missing: PageNumber, SectionTitle. Embedding type should be `Vector?` (pgvector). |
| `Artifact` → `ProjectArtifact` | `Entities/ProjectLearning.cs` | Rename. Missing: AgentName, Title, ContentFormat, ContentText, Language, FormatVersion. Different: BlobUri → StorageUrl, ContentType → ContentFormat enum. |
| `Decision` → `ProjectDecision` | `Entities/ProjectLearning.cs` | Complete rewrite. Spec: DecisionRef, Domain, Decision, Rationale, MadeBy, WorkflowRunId, Status, SupersededById. |
| `CostBudget` | `Entities/LlmCall.cs` | Remove entirely — budget enforcement is per-project field + per-session field, not a separate entity in the spec. |

### Entities to ADD (don't exist yet)

| Entity | Description |
|--------|-------------|
| `ProjectContext` | PCD entity — 1:1 with Project, holds JSON context data, version, size |
| `ContextChange` | PCD mutation history with path, old/new values, agent, reason |
| `ContextMilestone` | Named snapshot of PCD at a point in time |
| `ProjectIntent` | Project objective/scope with revision chain |
| `MilestoneSummary` | Periodic summary of project progress |
| `SessionChannel` | Communication channel binding for a session |
| `WorkflowRun` | Execution instance of a workflow (replaces WorkflowExecution) |
| `WorkflowSchedule` | Cron schedule for a workflow definition |
| `HumanInputRequest` | Approval gate in a workflow run |
| `ModelPricing` | Model cost lookup table (composite PK: model + effective_from) |
| `PromptVersion` | Versioned prompt history for any entity |

### Entities to REMOVE

| Entity | Reason |
|--------|--------|
| `WorkflowNode` | Spec stores nodes as JSON on WorkflowDefinition |
| `WorkflowEdge` | Spec stores edges as JSON on WorkflowDefinition |
| `WorkflowExecution` | Replaced by `WorkflowRun` |
| `WorkflowNodeExecution` | Not in spec (node state tracked via Tasks) |
| `AgentTemplate` | Not in spec (templates are WorkflowDefinitions or seed config) |
| `TemplateAgent` | Not in spec |
| `ProjectAgent.Agent` nav changes | FK goes to AgentCatalog via AgentCatalogId |
| `CostBudget` | Not a standalone entity in spec |
| `ProjectMember.Project` nav | Keep but ensure FK name matches |

### Enums to REWRITE

| Enum | Current | Target (from spec) |
|------|---------|-------------------|
| `TaskType` | `AgentTask, HumanTask, SystemTask` | `AgentTask, HumanDecision, AiDecision, Action, SubWorkflow` |
| `TaskStatus` | 10 values | `Proposed, Approved, Queued, Running, Completed, Failed, Skipped, Cancelled` |
| `SessionType` → `SessionStatus` | `Chat, Execution, Review` | `Active, Suspended, Completed, Expired` |
| `PlatformRole` → `SystemRole` | `Viewer..PlatformAdmin` | `Admin, Member` (platform-level only) |

### Enums to ADD

| Enum | Values |
|------|--------|
| `TaskSource` | `Workflow, Planner, Manual, AdHoc, Retry` |
| `IntentSource` | `Human, Agent, System` |
| `ChangeType` | `Add, Update, Remove` |
| `AgentRole` | `Lead, Worker, Reviewer, Verifier` |
| `FailureTier` | `Transient, Logical, Fatal` |
| `ProjectTier` | `Standard, Premium, Platform` |
| `ContentFormat` | `Markdown, Html, Json, Yaml, Code, PlainText` |
| `ExtractionStatus` | `Pending, Processing, Completed, Failed` |
| `DocumentType` | `Policy, Standard, Reference, Report, Data, Other` |
| `DecisionStatus` | `Active, Superseded, Retracted` |
| `HumanInputRequestStatus` | `Pending, Responded, TimedOut, Cancelled` |
| `WorkflowRunStatus` | `Pending, Running, Paused, Completed, Failed, Cancelled` |
| `AgentVisibility` | `Public, Internal, Hidden` |

---

## File-by-File Changes

### Files to CREATE

```
src/AgenticWorkforce.Domain/Entities/ProjectContext.cs     — ProjectContext, ContextChange, ContextMilestone
src/AgenticWorkforce.Domain/Entities/ProjectIntent.cs      — ProjectIntent
src/AgenticWorkforce.Domain/Entities/MilestoneSummary.cs   — MilestoneSummary
src/AgenticWorkforce.Domain/Entities/SessionChannel.cs     — SessionChannel
src/AgenticWorkforce.Domain/Entities/WorkflowRun.cs        — WorkflowRun, WorkflowSchedule, HumanInputRequest
src/AgenticWorkforce.Domain/Entities/ModelPricing.cs       — ModelPricing
src/AgenticWorkforce.Domain/Entities/PromptVersion.cs      — PromptVersion
src/AgenticWorkforce.Domain/Entities/ProjectArtifact.cs    — ProjectArtifact (extracted from ProjectLearning.cs)
src/AgenticWorkforce.Domain/Entities/ProjectDecision.cs    — ProjectDecision (extracted from ProjectLearning.cs)
src/AgenticWorkforce.Domain/Interfaces/Services/IEventPublisher.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IBudgetService.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IDocumentStore.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IEmbeddingService.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IWorkflowRepository.cs
```

### Files to REWRITE (complete replacement)

```
src/AgenticWorkforce.Domain/Entities/Enums.cs              — All enums rebuilt from spec
src/AgenticWorkforce.Domain/Entities/AgenticTask.cs        — Match spec exactly
src/AgenticWorkforce.Domain/Entities/Project.cs            — Match spec, rename ProjectMember
src/AgenticWorkforce.Domain/Entities/Session.cs            — Match spec, add SessionMessage fields
src/AgenticWorkforce.Domain/Entities/AgentCatalog.cs       — Match spec, remove Template entities
src/AgenticWorkforce.Domain/Entities/WorkflowDefinition.cs — JSON nodes/edges, remove Node/Edge entities
src/AgenticWorkforce.Domain/Entities/ProjectDocument.cs    — Match spec, fix DocumentChunk
src/AgenticWorkforce.Domain/Entities/ProjectLearning.cs    — Match spec, extract Artifact/Decision
src/AgenticWorkforce.Domain/Entities/ProjectEvent.cs       — Match spec
src/AgenticWorkforce.Domain/Entities/LlmCall.cs            — Match spec, remove CostBudget
src/AgenticWorkforce.Domain/Entities/PlatformUser.cs       — Rename to User.cs, match spec
src/AgenticWorkforce.Domain/Entities/EntityBase.cs         — Add Version (xmin), keep DateTime (not DateTimeOffset per AGENTS.md)
src/AgenticWorkforce.Domain/Interfaces/Services/IAgentRuntime.cs — Primitive types only (see Interface Signatures below)
src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectRepository.cs — Aggregate root CRUD only
```

### Files to DELETE

```
src/AgenticWorkforce.Domain/Entities/WorkflowExecution.cs  — Replaced by WorkflowRun.cs
```

---

## Critical Design Decisions

### DateTime vs DateTimeOffset

The architecture doc (003-database-schema.md) uses `DateTimeOffset` throughout. However, `AGENTS.md` explicitly mandates:
> "DateTime UTC only — DateTime.UtcNow, never DateTimeOffset, never DateTime.Now"

**Decision:** Follow AGENTS.md (the coding standard). Use `DateTime` throughout. The doc's `DateTimeOffset` is aspirational; the coding standard is the constraint. PostgreSQL TIMESTAMPTZ stores UTC regardless.

### `User` vs `PlatformUser` naming

The spec uses `User`. AGENTS.md uses "PlatformUser" in examples. The database table will be `Users`.

**Decision:** Rename entity class to `User` to match the spec. The old `PlatformUser` name was a scaffold placeholder.

### Separate Node/Edge entities vs JSON

The current scaffold has `WorkflowNode` and `WorkflowEdge` as separate entities with their own tables. The spec stores them as JSON arrays on `WorkflowDefinition`.

**Decision:** Follow the spec. JSON storage is correct — the React Flow editor serialises the entire graph, and workflow definitions are loaded atomically. Separate tables would cause N+1 problems and break atomic versioning.

### `CostBudget` entity removal

The spec does not have a standalone `CostBudget` entity. Budget ceiling is a field on `Project` and `Session`. Budget enforcement is middleware behaviour, not a DB entity.

**Decision:** Remove `CostBudget`. Add `BudgetCeilingUsd` to `Project` and `CostBudgetUsd` to `Session`.

---

## Interface Signatures (Target)

### IAgentRuntime (Domain — primitive types only)

The Domain interface uses only primitive types so it has zero knowledge of the Agents project. The richer interface (`IAgentRuntime` with `ProjectContext` value object, `AgentRunOptions`) is internal to `AgenticWorkforce.Agents/Runtime/` and is NOT in Domain.

```csharp
// Domain interface — stable, no dependency on Agents project
public interface IAgentRuntime
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request, CancellationToken ct = default);
}

public record AgentExecutionRequest(
    Guid ProjectId,
    Guid TaskId,
    string AgentName,
    string Objective,
    string? Input = null,
    Guid? SessionId = null,
    TimeSpan? Timeout = null);

public record AgentExecutionResult(
    bool Success,
    string? Output,
    string? Error,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double DurationSeconds,
    int ToolCallCount);
```

The Agents project internally defines a richer interface for its own use:

```csharp
// Internal to AgenticWorkforce.Agents — NOT in Domain
internal interface IAgentRuntimeInternal
{
    Task<AgentResult> RunAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default);

    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        string agentName, string objective, ProjectContext context,
        AgentRunOptions? options = null, CancellationToken ct = default);
}
```

The public `IAgentRuntime` implementation delegates to the internal one, mapping between primitive records and rich value objects.

### IWorkflowEngine

```csharp
public interface IWorkflowEngine
{
    Task<Guid> StartAsync(Guid projectId, Guid workflowDefinitionId, string? triggerType, string? context, CancellationToken ct = default);
    Task PauseAsync(Guid workflowRunId, CancellationToken ct = default);
    Task ResumeAsync(Guid workflowRunId, CancellationToken ct = default);
    Task CancelAsync(Guid workflowRunId, CancellationToken ct = default);
    Task SubmitHumanInputAsync(Guid requestId, string response, Guid responderId, CancellationToken ct = default);
}
```

### IEventPublisher

```csharp
public interface IEventPublisher
{
    Task PublishAsync(ProjectEvent evt, CancellationToken ct = default);
    Task PublishAsync(string channel, string eventType, object data, CancellationToken ct = default);
}
```

### IBudgetService

```csharp
public interface IBudgetService
{
    Task<bool> CanSpendAsync(Guid projectId, Guid? sessionId, decimal estimatedCostUsd, CancellationToken ct = default);
    Task RecordSpendAsync(Guid projectId, Guid? sessionId, Guid? taskId, decimal costUsd, CancellationToken ct = default);
    Task<BudgetStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default);
}

public record BudgetStatus(decimal CeilingUsd, decimal UsedUsd, decimal RemainingUsd, bool IsExhausted);
```

### IEmbeddingService (single definition — shared by Api search endpoints and Agents context assembly)

```csharp
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

Note: No `IKnowledgeRepository` — knowledge queries (learnings, decisions, milestones) are simple reads handled directly by vertical-slice endpoints via `AppDbContext`. Only aggregate roots (Project, Task, Session, Workflow) get repository abstractions.

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0 with zero errors
2. Every entity class in `src/AgenticWorkforce.Domain/Entities/` matches the corresponding class in `docs/002-architecture/003-database-schema.md` (field names, types, navigation properties)
3. Every enum in `Enums.cs` matches the spec
4. All interface files compile and reference only types from within the Domain project
5. No `using System.Numerics` in any domain file
6. No `DateTimeOffset` in any domain file
7. No references to removed entities (WorkflowNode, WorkflowEdge, WorkflowExecution, WorkflowNodeExecution, AgentTemplate, TemplateAgent, CostBudget) anywhere in the solution
8. `grep -r 'DateTimeOffset' src/AgenticWorkforce.Domain/` returns zero results
9. `grep -r 'DateTime.Now[^a-zA-Z]' src/` returns zero results (only UtcNow)

---

## Goal Command

```
/goal All entities in src/AgenticWorkforce.Domain/ match docs/002-architecture/003-database-schema.md exactly (using DateTime not DateTimeOffset per AGENTS.md). dotnet build AgenticWorkforce.slnx exits 0 with zero errors and zero warnings about missing types. No references to removed entities (WorkflowNode, WorkflowEdge, WorkflowExecution, WorkflowNodeExecution, AgentTemplate, TemplateAgent, CostBudget) exist anywhere in the solution. Verify by running dotnet build and showing exit code 0.
```
