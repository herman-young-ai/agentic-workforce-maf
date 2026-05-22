# Phase 4: API Vertical Slices (Extended)

**Status:** Not Started
**Depends On:** Phase 3.5 (Boundary Remediation)
**Verification:** `dotnet test AgenticWorkforce.slnx` passes, Swagger shows all endpoints, auth policies enforced, `scripts/check-rules.sh` passes DL-001.

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from Phase 3.5. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify Phase 3.5's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0
   - `grep -rn 'AppDbContext' src/AgenticWorkforce.Api/ --include='*.cs' | grep -v 'Program.cs' | grep -v 'HealthCheck'` returns zero
   - `WorkflowRun.TriggeredById` and `ProjectLearning.PromotionStatus` exist in the schema
4. Check `.githooks/pre-commit` for the active CQI floor and confirm it is consistent with the current score.
5. Confirm `IIdempotencyService` is user-scoped (takes `Guid userId` as a parameter) — Phase 3.5 changed the signature.

---

## Architecture Pattern (post-Phase-3.5)

Every endpoint follows the vertical-slice file shape **but never injects `AppDbContext`**. All persistence flows through repositories defined in `AgenticWorkforce.Domain.Interfaces.Repositories.*` and implemented in `AgenticWorkforce.Infrastructure.Repositories.*`.

```csharp
public static class ListLearnings
{
    public record Response(/* ... */);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/learnings", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository learnings,           // never AppDbContext
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);
        var page = await learnings.ListByProjectPagedAsync(projectId, paging, ct);
        return Results.Ok(page);
    }
}
```

This pattern is non-negotiable — see Phase 3.5 §Why This Phase Exists.

---

## Objective

Implement the remaining API endpoints — workflows, knowledge management, documents, events, costs, catalog browsing, executions, and admin operations. After this phase the API surface is complete: every endpoint defined in [docs/002-architecture/004-api-design.md](../002-architecture/004-api-design.md) exists and returns correct responses.

**Authoritative endpoint spec:** Where this plan's tables and [004-api-design.md](../002-architecture/004-api-design.md) disagree on path, method, or auth policy, **004-api-design.md wins**. Cross-check before implementing each section.

---

## Endpoints by Feature

### 4.1 Workflows (`/api/v1/projects/{projectId}/workflows`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListWorkflows.cs` | GET | `/` | Viewer+ |
| `GetWorkflow.cs` | GET | `/{workflowId}` | Viewer+ |
| `CreateWorkflow.cs` | POST | `/` | Operator+ |
| `UpdateWorkflow.cs` | PATCH | `/{workflowId}` | Operator+ |
| `DeleteWorkflow.cs` | DELETE | `/{workflowId}` | Owner |
| `ValidateWorkflow.cs` | POST | `/{workflowId}/validate` | Operator+ |
| `SaveCanvas.cs` | PUT | `/{workflowId}/canvas` | Operator+ |
| `RunWorkflow.cs` | POST | `/{workflowId}/run` | Operator+ |
| `ListWorkflowRuns.cs` | GET | `/{workflowId}/runs` | Viewer+ |

### 4.2 Workflow Runs (`/api/v1/projects/{projectId}/workflow-runs`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListAllRuns.cs` | GET | `/` | Viewer+ |
| `GetRun.cs` | GET | `/{runId}` | Viewer+ |

### 4.3 Schedules (`/api/v1/projects/{projectId}/schedules`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListSchedules.cs` | GET | `/` | Viewer+ |
| `CreateSchedule.cs` | POST | `/` | Owner |
| `UpdateSchedule.cs` | PATCH | `/{scheduleId}` | Owner |
| `DeleteSchedule.cs` | DELETE | `/{scheduleId}` | Owner |
| `ListUpcoming.cs` | GET | `/upcoming` | Viewer+ |

### 4.4 Human Input / Approval Gates (`/api/v1/projects/{projectId}/human-input`)

| File | Method | Path | Auth | Body |
|------|--------|------|------|------|
| `ListPending.cs` | GET | `/pending` | Reviewer+ | — |
| `Respond.cs` | POST | `/{requestId}/respond` | Reviewer+ | `{ decision: HumanDecisionType, response?: string }` |
| `GetAudit.cs` | GET | `/{requestId}/audit` | Viewer+ | — |

**`Respond.cs` contract:**

```csharp
public record RespondRequest(HumanDecisionType Decision, string? Response);

// Handler delegates to IWorkflowEngine.SubmitHumanInputAsync(
//   requestId, request.Decision, request.Response, currentUser.Id, ct)
// which sets HumanInputRequest.Decision (enum, queryable), HumanInputRequest.Response
// (free-text), HumanInputRequest.Status = Completed, and raises the Durable Task
// external event to resume the paused workflow orchestration.
```

**Segregation of duties:** the responder MUST NOT be the user who triggered the request. Enforced via the new `WorkflowRun.TriggeredById Guid?` FK added in Phase 3.5: the handler resolves the request's `WorkflowRun`, compares `workflowRun.TriggeredById == currentUser.Id`, and returns **403** if equal. The string-typed `WorkflowRun.TriggeredBy` field is **not** used for this comparison.

If `WorkflowRun.TriggeredById` is null (schedule-triggered runs with no owning user), SOD is vacuously satisfied — any Reviewer+ may approve.

### 4.5 Project Context — PCD (`/api/v1/projects/{projectId}/context`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `GetContext.cs` | GET | `/` | Viewer+ |
| `GetContextHistory.cs` | GET | `/history` | Viewer+ |
| `AddPrinciple.cs` | POST | `/principles` | Operator+ |
| `AddGuardrail.cs` | POST | `/guardrails` | Operator+ |
| `RemovePrinciple.cs` | DELETE | `/principles/{principleId}` | Owner |
| `RemoveGuardrail.cs` | DELETE | `/guardrails/{guardrailId}` | Owner |

### 4.6 Knowledge & Learnings (`/api/v1/projects/{projectId}/learnings`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListLearnings.cs` | GET | `/` | Viewer+ |
| `GetLearning.cs` | GET | `/{learningId}` | Viewer+ |
| `RetractLearning.cs` | POST | `/{learningId}/retract` | Operator+ |
| `EditLearning.cs` | PATCH | `/{learningId}` | Owner |
| `SupersedeLearning.cs` | POST | `/{learningId}/supersede` | Operator+ |
| `PromoteLearning.cs` | POST | `/{learningId}/promote` | Operator+ |
| `SearchLearnings.cs` | POST | `/search` | Viewer+ |
| `FindSimilar.cs` | GET | `/{learningId}/similar` | Viewer+ |

**`PromoteLearning.cs`** transitions `ProjectLearning.PromotionStatus` from `None` → `PendingApproval`. Records `PromotionRequestedAt = DateTime.UtcNow` and `PromotionRequestedById = currentUser.Id`. The actual `Approved` transition happens in §4.18 `ApprovePromotion` (PlatformAdmin).

### 4.7 Milestones (`/api/v1/projects/{projectId}/milestones`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListMilestones.cs` | GET | `/` | Viewer+ |
| `GetMilestone.cs` | GET | `/{milestoneId}` | Viewer+ |
| `CreateMilestone.cs` | POST | `/` | Operator+ |

### 4.8 Decisions (`/api/v1/projects/{projectId}/decisions`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListDecisions.cs` | GET | `/` | Viewer+ |
| `GetDecision.cs` | GET | `/{decisionId}` | Viewer+ |
| `CreateDecision.cs` | POST | `/` | Operator+ |

### 4.9 Intent (`/api/v1/projects/{projectId}/intent`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `GetIntent.cs` | GET | `/` | Viewer+ |
| `GetIntentHistory.cs` | GET | `/history` | Viewer+ |
| `CreateIntent.cs` | POST | `/` | Operator+ |

### 4.10 Artifacts (`/api/v1/projects/{projectId}/artifacts`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListArtifacts.cs` | GET | `/` | Viewer+ |
| `GetArtifact.cs` | GET | `/{artifactId}` | Viewer+ |
| `GetArtifactContent.cs` | GET | `/{artifactId}/content` | Viewer+ |
| `RetractArtifact.cs` | POST | `/{artifactId}/retract` | Owner |

### 4.11 Documents (`/api/v1/projects/{projectId}/documents`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `UploadDocument.cs` | POST | `/` | Operator+ |
| `ListDocuments.cs` | GET | `/` | Viewer+ |
| `GetDocument.cs` | GET | `/{documentId}` | Viewer+ |
| `GetDocumentText.cs` | GET | `/{documentId}/text` | Viewer+ |
| `RetractDocument.cs` | POST | `/{documentId}/retract` | Owner |
| `SearchDocuments.cs` | POST | `/search` | Viewer+ |

`UploadDocument.cs` accepts `multipart/form-data`. Kestrel `MaxRequestBodySize` raised to 50 MB on this endpoint only (per Principle 19, API max upload bound). Endpoint maps with `.DisableAntiforgery()`.

### 4.12 Events / Console (`/api/v1/projects/{projectId}/events`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListEvents.cs` | GET | `/` | Viewer+ |

Note: SSE streaming endpoints (`/stream`) are Phase 5.

### 4.13 Costs (`/api/v1/projects/{projectId}/costs`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `GetCostSummary.cs` | GET | `/summary` | Viewer+ |
| `GetCostTimeline.cs` | GET | `/timeline` | Viewer+ |
| `GetTokenEconomics.cs` | GET | `/token-economics` | Viewer+ |

**Partition-aware queries required.** `LlmCall` is RANGE-partitioned by month (per [ADR-008](../002-architecture/ADR-008-audit-compliance.md) and migration `20260517071100_CreatePartitionedTables`). All three endpoints accept `from`/`to` query parameters (default: last 30 days) and require the underlying queries to filter on `created_at` so PostgreSQL prunes partitions. Without partition pruning, a 6-month sweep scans every partition.

`ICostQueryService` must enforce: (a) `from`/`to` are required at the repository call site (no default-to-all-time path), (b) max range is configurable but defaults to 365 days, (c) the `(project_id, created_at)` composite index is verified by an integration test. Add migration `AddLlmCallProjectCreatedAtIndex` if the index is missing.

### 4.14 Executions (`/api/v1/projects/{projectId}/executions`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `DispatchTasks.cs` | POST | `/dispatch` | Operator+ |
| `RunAdHoc.cs` | POST | `/run` | Operator+ |
| `GetExecution.cs` | GET | `/{executionId}` | Viewer+ |

**Api responsibility (this phase):** create the execution intent record, mark approved tasks as `Queued`, **enqueue a dispatch message to the Redis Stream**, and return the queue message ID as `executionId`. The Api **does NOT** create the `WorkflowRun` row — Worker creates it when it picks up the queue message (Phase 8). This preserves Principle 16 (single source of truth) — `WorkflowRun` has one writer.

`GetExecution.cs` returns the current state by querying the Redis Stream consumer-group position (Phase 4 stub: returns `Pending` always) and any matching `WorkflowRun` row if Worker has already consumed it. Phase 8 wires the real consumer.

### 4.15 Catalog — Browse (`/api/v1/catalog`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListCatalog.cs` | GET | `/` | Member |
| `GetCatalogAgent.cs` | GET | `/{agentId}` | Member |

These are **read-only browse endpoints** scoped to all authenticated users for catalog discovery. Filters `Visibility != Private` and `Enabled = true`. Uses `IAgentCatalogRepository` from Phase 3.5.

### 4.16 Admin: Dashboard (`/api/v1/admin/dashboard`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `GetHealth.cs` | GET | `/health` | PlatformAdmin |
| `GetOverview.cs` | GET | `/overview` | PlatformAdmin |
| `GetAdminCosts.cs` | GET | `/costs` | PlatformAdmin |
| `GetAdminCostTimeline.cs` | GET | `/costs/timeline` | PlatformAdmin |

`GetAdminCosts` and `GetAdminCostTimeline` apply the same partition-aware constraints as §4.13 but cross-project.

### 4.17 Admin: Agent Catalog (`/api/v1/admin/catalog`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `AdminListCatalog.cs` | GET | `/` | PlatformAdmin |
| `AdminCreateAgent.cs` | POST | `/` | PlatformAdmin |
| `AdminGetAgent.cs` | GET | `/{agentId}` | PlatformAdmin |
| `AdminUpdateAgent.cs` | PATCH | `/{agentId}` | PlatformAdmin |
| `AdminDeleteAgent.cs` | DELETE | `/{agentId}` | PlatformAdmin |
| `AdminUpdatePrompt.cs` | PUT | `/{agentId}/prompt` | PlatformAdmin |
| `AdminPromptHistory.cs` | GET | `/{agentId}/prompt-history` | PlatformAdmin |
| `AdminEnableAgent.cs` | POST | `/{agentId}/enable` | PlatformAdmin |
| `AdminDisableAgent.cs` | POST | `/{agentId}/disable` | PlatformAdmin |
| `AdminSeedCatalog.cs` | POST | `/seed` | PlatformAdmin |

**`AdminSeedCatalog` is the platform-level catalog seeder** — it seeds the global `AgentCatalog` table from a known YAML set (Phase 7 produces the YAML files). Distinct from the existing project-scoped [`Team/SeedTeam.cs`](../../src/AgenticWorkforce.Api/Features/Team/SeedTeam.cs) which seeds a specific project's `ProjectAgent` rows from a template. Both endpoints exist; they operate at different scopes (catalog vs project).

For Phase 4, `AdminSeedCatalog` is a stub: returns `501 Not Implemented` with `code: CATALOG_SEED_NOT_READY`, body documenting that catalog YAML lands in Phase 7. The endpoint exists so admin tooling and tests can target it; the actual seeding implementation moves with Phase 7.

### 4.18 Admin: Platform Knowledge (`/api/v1/admin/knowledge`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListPlatformLearnings.cs` | GET | `/learnings` | PlatformAdmin |
| `ListPendingPromotions.cs` | GET | `/promotions/pending` | PlatformAdmin |
| `ApprovePromotion.cs` | POST | `/learnings/{learningId}/approve-promotion` | PlatformAdmin |
| `RejectPromotion.cs` | POST | `/learnings/{learningId}/reject-promotion` | PlatformAdmin |
| `EditPlatformLearning.cs` | PATCH | `/learnings/{learningId}` | PlatformAdmin |
| `RetractPlatformLearning.cs` | POST | `/learnings/{learningId}/retract` | PlatformAdmin |

**Promotion approval state machine** (using the `PromotionStatus` enum added in Phase 3.5):

- `PromoteLearning` (§4.6, Operator+): `None` → `PendingApproval` ; sets `PromotionRequestedAt`, `PromotionRequestedById`
- `ListPendingPromotions`: lists learnings with `PromotionStatus == PendingApproval`, ordered by `PromotionRequestedAt`
- `ApprovePromotion`: `PendingApproval` → `Approved` ; sets `PromotedBy = currentUser.Id`, `PromotedAt = DateTime.UtcNow`
- `RejectPromotion`: `PendingApproval` → `Rejected` ; sets `PromotionRejectedReason` from body
- `RetractPlatformLearning`: `Approved` → `None` (or `LearningStatus.Retracted` if removing the learning itself)

Endpoint URL convention: `learningId` in the path identifies the learning being promoted. The plan's earlier `promotionId` terminology was wrong — there is no separate Promotion entity; the state lives on `ProjectLearning`.

---

## Supporting Infrastructure

### New repository interfaces

All in `AgenticWorkforce.Domain.Interfaces.Repositories`. Implementations in `AgenticWorkforce.Infrastructure.Repositories`.

| Interface | DbSets | Notes |
|---|---|---|
| `IWorkflowDefinitionRepository` | `WorkflowDefinitions` | `GetByIdAsync`, `ListByProjectPagedAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` (soft via `LockedAt`) |
| `IWorkflowRunRepository` | `WorkflowRuns` | `GetByIdAsync`, `ListByProjectPagedAsync(projectId, workflowId? filter)` — write methods deferred to Phase 8 (Worker owns writes) |
| `IWorkflowScheduleRepository` | `WorkflowSchedules` | `GetByIdAsync`, `ListByProjectAsync`, `ListUpcomingAsync(projectId, DateTime horizon)`, `AddAsync`, `UpdateAsync`, `RemoveAsync` |
| `IHumanInputRepository` | `HumanInputRequests` | `GetByIdAsync`, `ListPendingByProjectAsync`, `RespondAsync(requestId, decision, response, responderId)` — single transaction with SOD enforcement |
| `IProjectContextRepository` | `ProjectContexts`, `ContextChanges` | `GetAsync(projectId)`, `GetHistoryAsync(projectId)`, `AddPrincipleAsync`, `AddGuardrailAsync`, `RemovePrincipleAsync`, `RemoveGuardrailAsync` — versioned writes |
| `ILearningRepository` | `ProjectLearnings` | `GetByIdAsync`, `ListByProjectPagedAsync`, `AddAsync`, `UpdateAsync`, `RetractAsync`, `SupersedeAsync`, `RequestPromotionAsync`, `ApprovePromotionAsync`, `RejectPromotionAsync`, `ListPendingPromotionsPagedAsync`, `SearchByEmbeddingAsync(Vector, limit, ct)`, `FindSimilarAsync(learningId, limit, ct)` |
| `IMilestoneRepository` | `MilestoneSummaries`, `ContextMilestones` | `GetByIdAsync`, `ListByProjectPagedAsync`, `AddAsync` |
| `IDecisionRepository` | `ProjectDecisions` | `GetByIdAsync`, `ListByProjectPagedAsync`, `AddAsync` |
| `IIntentRepository` | `ProjectIntents` | `GetCurrentAsync(projectId)`, `GetHistoryAsync(projectId)`, `AddAsync` |
| `IArtifactRepository` | `ProjectArtifacts` | `GetByIdAsync`, `ListByProjectPagedAsync`, `GetContentAsync`, `RetractAsync` |
| `IDocumentRepository` | `ProjectDocuments`, `DocumentChunks` | `GetByIdAsync`, `ListByProjectPagedAsync`, `AddAsync`, `RetractAsync`, `SearchChunksAsync(projectId, Vector queryEmbedding, int limit, ct)` (signature uses `Pgvector.Vector`, not `float[]`) |
| `IEventRepository` | `ProjectEvents` | `ListByProjectPagedAsync(projectId, EventFilter filter, paging, ct)` |
| `ICatalogQueryRepository` | `AgentCatalogs` | Read-only browse view: `ListVisibleAsync(memberRole, paging)`, `GetByIdVisibleAsync(memberRole, id)` — applies `Visibility` filter for non-admin readers (Admin uses `IAgentCatalogRepository` from Phase 3.5 directly) |
| `IExecutionRepository` | (Redis Stream client) | `EnqueueDispatchAsync(projectId, taskIds, requesterId)`, `EnqueueAdHocAsync(...)`, `GetStatusAsync(executionId)` — wraps `Redis Streams XADD/XREAD`; implementation is a thin client this phase, Phase 8 adds the consumer side |

### New service interfaces

```csharp
// IProjectContextService — PCD JSON mutations with versioned history
public interface IProjectContextService
{
    Task<ProjectContext> GetAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct);
    Task AddPrincipleAsync(Guid projectId, string principle, Guid addedById, CancellationToken ct);
    Task AddGuardrailAsync(Guid projectId, string guardrail, Guid addedById, CancellationToken ct);
    Task RemovePrincipleAsync(Guid projectId, string principleId, Guid removedById, CancellationToken ct);
    Task RemoveGuardrailAsync(Guid projectId, string guardrailId, Guid removedById, CancellationToken ct);
}

// ICostQueryService — partition-aware aggregations over LlmCall
public interface ICostQueryService
{
    Task<CostSummary> GetSummaryAsync(Guid projectId, DateTime from, DateTime to, CancellationToken ct);
    Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAsync(Guid projectId, DateTime from, DateTime to, CancellationToken ct);
    Task<TokenEconomics> GetTokenEconomicsAsync(Guid projectId, DateTime from, DateTime to, CancellationToken ct);
}

public record CostSummary(
    decimal TotalUsd,
    IReadOnlyList<AgentCostBreakdown> ByAgent,
    IReadOnlyList<ModelCostBreakdown> ByModel);
public record CostTimelineEntry(DateTime Hour, decimal CostUsd, int Calls);
public record TokenEconomics(long TotalInput, long TotalOutput, long CacheRead, long CacheCreation, double CacheHitRate);

// IWorkflowValidator — graph integrity
public interface IWorkflowValidator
{
    ValidationResult Validate(string nodesJson, string edgesJson);
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
```

Validation enforces (each is a distinct test case):

- Exactly one `Start` node
- At least one `End` node
- All edges reference existing node IDs (no dangling references)
- No orphan nodes (every node reachable from `Start`)
- `Decision` nodes (Human or AI) have outgoing edges with labeled conditions
- No cycles (the graph is a DAG)

### Pre-existing services (do NOT recreate — verify and adjust)

These already exist in `src/AgenticWorkforce.Infrastructure/Services/`. Phase 4 work is to verify, adjust if needed, and wire into the new endpoints.

#### `IDocumentStore` / `LocalFileDocumentStore`

Already in [LocalFileDocumentStore.cs](../../src/AgenticWorkforce.Infrastructure/Services/LocalFileDocumentStore.cs). The interface is:

```csharp
Task<string> UploadAsync(string containerName, string path, Stream content, string contentType, CancellationToken ct);
Task<Stream> DownloadAsync(string containerName, string path, CancellationToken ct);
Task DeleteAsync(string containerName, string path, CancellationToken ct);
Task<bool> ExistsAsync(string containerName, string path, CancellationToken ct);
```

Constructor takes `basePath` — bind it from `IOptions<DocumentStorageOptions>` rooted at `IHostEnvironment.ContentRootPath`. **Do not pass a hardcoded relative path** like `Path.Combine("var", "uploads")` — the existing impl already guards against path traversal and resolves via `Path.GetFullPath`. Phase 4 wires it through DI configuration only; no code changes to `LocalFileDocumentStore` itself.

#### `IEmbeddingService` / `StubEmbeddingService` — **breaking change required**

Already in [StubEmbeddingService.cs](../../src/AgenticWorkforce.Infrastructure/Services/StubEmbeddingService.cs). Current impl returns 1536 zeros — this **silently corrupts** pgvector cosine searches (zero-norm vectors give undefined distance; results become arbitrary). Per Principle 8 (Fail Fast), replace with:

```csharp
internal sealed class StubEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => throw new NotImplementedException(
            "Embeddings are not configured. Wire AzureOpenAIEmbeddingService in Phase 6 " +
            "(per ADR-002) before calling embedding-dependent endpoints.");

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => throw new NotImplementedException(/* same message */);
}
```

**Phase 4 endpoints that depend on embeddings (`SearchLearnings`, `FindSimilar`, `SearchDocuments`)** must short-circuit at the endpoint with HTTP **503** when the resolved `IEmbeddingService` is `StubEmbeddingService`. Detection pattern:

```csharp
if (embeddings is StubEmbeddingService)
    return Results.Problem(
        statusCode: 503,
        title: "Semantic search not yet available.",
        detail: "Embedding provider is wired in Phase 6.",
        extensions: new Dictionary<string, object?> { ["code"] = "EMBEDDING_NOT_CONFIGURED" });
```

This is preferable to a feature flag because the stub itself is the configuration signal. When `AzureOpenAIEmbeddingService` replaces it in Phase 6, all three endpoints become live without code changes here.

The existing code comment in `StubEmbeddingService` says "wired up in Phase 6" — that is correct. The previous version of this plan referred to "Phase 11" — that was wrong; treat Phase 6 as canonical.

---

## File Summary

### Endpoint files to CREATE (~85)

```
src/AgenticWorkforce.Api/Features/Workflows/        (9 files)
src/AgenticWorkforce.Api/Features/WorkflowRuns/     (2 files)
src/AgenticWorkforce.Api/Features/Schedules/        (5 files)
src/AgenticWorkforce.Api/Features/HumanInput/       (3 files)
src/AgenticWorkforce.Api/Features/Context/          (6 files)
src/AgenticWorkforce.Api/Features/Learnings/        (8 files)
src/AgenticWorkforce.Api/Features/Milestones/       (3 files)
src/AgenticWorkforce.Api/Features/Decisions/        (3 files)
src/AgenticWorkforce.Api/Features/Intent/           (3 files)
src/AgenticWorkforce.Api/Features/Artifacts/        (4 files)
src/AgenticWorkforce.Api/Features/Documents/        (6 files)
src/AgenticWorkforce.Api/Features/Events/           (1 file)
src/AgenticWorkforce.Api/Features/Costs/            (3 files)
src/AgenticWorkforce.Api/Features/Executions/       (3 files)
src/AgenticWorkforce.Api/Features/Catalog/          (2 files)
src/AgenticWorkforce.Api/Features/Admin/Dashboard/  (4 files)
src/AgenticWorkforce.Api/Features/Admin/Catalog/    (10 files)
src/AgenticWorkforce.Api/Features/Admin/Knowledge/  (6 files)
```

### Domain interfaces to CREATE (13)

```
src/AgenticWorkforce.Domain/Interfaces/Repositories/IWorkflowDefinitionRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IWorkflowRunRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IWorkflowScheduleRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IHumanInputRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectContextRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/ILearningRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IMilestoneRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IDecisionRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IIntentRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IArtifactRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IDocumentRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IEventRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/ICatalogQueryRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IExecutionRepository.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IProjectContextService.cs
src/AgenticWorkforce.Domain/Interfaces/Services/ICostQueryService.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IWorkflowValidator.cs
```

### Infrastructure implementations to CREATE (17)

One implementation per interface above, all in `src/AgenticWorkforce.Infrastructure/Repositories/` or `src/AgenticWorkforce.Infrastructure/Services/`.

### Files to MODIFY

- `src/AgenticWorkforce.Infrastructure/Services/StubEmbeddingService.cs` — replace zero-vector returns with `NotImplementedException` (see §Pre-existing services)
- `src/AgenticWorkforce.Infrastructure/DependencyInjection.cs` — register new repositories and services
- `src/AgenticWorkforce.Api/Program.cs` — wire `IDocumentStore` with `IOptions<DocumentStorageOptions>` from `appsettings.json`; add 50 MB body size override on the upload endpoint
- `src/AgenticWorkforce.Api/Core/Extensions/EndpointRegistrationExtensions.cs` — add `MapWorkflowEndpoints`, `MapWorkflowRunEndpoints`, `MapScheduleEndpoints`, `MapHumanInputEndpoints`, `MapContextEndpoints`, `MapLearningEndpoints`, `MapMilestoneEndpoints`, `MapDecisionEndpoints`, `MapIntentEndpoints`, `MapArtifactEndpoints`, `MapDocumentEndpoints`, `MapEventEndpoints`, `MapCostEndpoints`, `MapExecutionEndpoints`, `MapCatalogEndpoints`, `MapAdminDashboardEndpoints`, `MapAdminCatalogEndpoints`, `MapAdminKnowledgeEndpoints` (18 new registration methods)
- `appsettings.Development.json` — add `DocumentStorage.BasePath` (default `var/uploads`), `CostQuery.MaxRangeDays` (default 365)

### Migration to CREATE

- `src/AgenticWorkforce.Infrastructure/Migrations/{Timestamp}_AddLlmCallProjectCreatedAtIndex.cs` — only if the composite index `(project_id, created_at)` is not already present from Phase 2

---

## Test Coverage

Phase 4 ships ~85 endpoint files. Two integration test files are not enough. Minimum coverage:

### Integration tests (`tests/AgenticWorkforce.Api.Tests.Integration/Features/`)

One file per feature folder — 18 files. Each must cover at minimum:

- Happy-path read/write
- 401 unauthenticated
- 403 for each role below the endpoint's minimum
- BOLA: cross-project access denied (request hits project X with membership in project Y → 403)
- Idempotency replay for POST endpoints with `X-Idempotency-Key`
- 404 for nonexistent resource ID

### Specific safety tests

- `tests/AgenticWorkforce.Api.Tests.Integration/Features/Learnings/EmbeddingStubGateTests.cs` — `SearchLearnings`, `FindSimilar`, `SearchDocuments` all return **503** with `code: EMBEDDING_NOT_CONFIGURED` when `StubEmbeddingService` is the registered impl; never return arbitrary results
- `tests/AgenticWorkforce.Api.Tests.Integration/Features/Workflows/WorkflowValidatorTests.cs` — one rejection-cause assertion per rule (single Start, ≥1 End, dangling edges, orphans, missing decision labels, cycles)
- `tests/AgenticWorkforce.Api.Tests.Integration/Features/HumanInput/SegregationOfDutiesTests.cs` — user who triggered run cannot Respond; user who is Reviewer+ and did not trigger CAN Respond; schedule-triggered runs (TriggeredById null) allow any Reviewer+
- `tests/AgenticWorkforce.Api.Tests.Integration/Features/Documents/UploadSizeLimitTests.cs` — 50 MB upload succeeds, 51 MB returns 413 Payload Too Large
- `tests/AgenticWorkforce.Api.Tests.Integration/Features/Costs/PartitionPruningTests.cs` — query plan inspection confirms partition pruning for date-bounded queries (use `EXPLAIN` and assert the partition list)
- `tests/AgenticWorkforce.Api.Tests.Integration/Features/Admin/AdminAuthorizationTests.cs` — every admin endpoint returns 403 for non-admin users

### Unit tests

- `tests/AgenticWorkforce.Api.Tests.Unit/Workflows/WorkflowValidatorUnitTests.cs` — pure validator logic, no DB
- `tests/AgenticWorkforce.Domain.Tests.Unit/Enums/PromotionStatusTransitionTests.cs` — verify the state machine: `None → PendingApproval → {Approved, Rejected}`; no invalid transitions accepted by `ILearningRepository`

Total: **24 test files** (18 feature × 1 + 6 specific safety).

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` exits 0 — all 24 new test files pass; all Phase 3 + Phase 3.5 tests still pass
3. Swagger UI shows all endpoints from §§4.1-4.18 grouped by tag
4. `grep -rn 'AppDbContext' src/AgenticWorkforce.Api/ --include='*.cs' | grep -v 'Program.cs' | grep -v 'HealthCheck'` returns **zero results** (Phase 3.5 invariant preserved)
5. `scripts/check-rules.sh` passes DL-001, DL-002, MB-001 through MB-005
6. CQI score does not regress below the active floor; ideally improves (Reusability dimension should rise as repositories are added)
7. Admin endpoints return 403 for non-admin users
8. PCD endpoints correctly mutate the JSON context and version the change history
9. Workflow validation rejects each invalid graph cause distinctly (no merging into a generic "invalid graph" error)
10. Document upload accepts multipart up to 50 MB and stores metadata + content via `IDocumentStore`
11. Cost endpoints reject queries with no `from`/`to` and queries spanning more than `CostQuery.MaxRangeDays`
12. `SearchLearnings`, `FindSimilar`, `SearchDocuments` return 503 when `StubEmbeddingService` is registered (no silent zero-vector results)
13. SOD test confirms the user who triggered a workflow run cannot approve its human input requests
14. Promotion state machine: a learning can move `None → PendingApproval → {Approved, Rejected}` and **not** transition by any other path

---

## Goal Command

```
/goal Phase 4 extended API slices complete: Workflows (9), WorkflowRuns (2), Schedules (5), HumanInput (3) with SOD via WorkflowRun.TriggeredById, Context/PCD (6), Learnings (8) with PromotionStatus state machine, Milestones (3), Decisions (3), Intent (3), Artifacts (4), Documents (6) with 50 MB upload limit and IDocumentStore wiring, Events (1), Costs (3) partition-aware with required date range, Executions (3) with Api as enqueuer-only (Worker owns WorkflowRun writes), Catalog (2), Admin Dashboard (4), Admin Catalog (10) with AdminSeedCatalog as 501 stub until Phase 7, Admin Knowledge (6) with promotion approval gates. All endpoints use repositories — zero AppDbContext injection in Features/. Workflow validator enforces six distinct rejection causes. StubEmbeddingService throws NotImplementedException; embedding-dependent endpoints return 503 with EMBEDDING_NOT_CONFIGURED. 18 integration test files (one per feature folder) plus 6 specific safety tests plus 2 unit test files. Verify: dotnet build + test exit 0, Swagger shows all endpoints, DL-001 grep returns zero, partition pruning verified via EXPLAIN, SOD test confirms triggered != approved. Stop after 70 turns.
```
