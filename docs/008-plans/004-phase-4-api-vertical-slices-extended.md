# Phase 4: API Vertical Slices (Extended)

**Status:** Not Started
**Depends On:** Phase 3 (API Core)
**Verification:** `dotnet test AgenticWorkforce.slnx` passes, Swagger shows all endpoints, auth policies enforced

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

Implement the remaining API endpoints that support workflows, knowledge management, documents, events, costs, catalog browsing, executions, and admin operations. After this phase the API surface is complete — every endpoint defined in `docs/002-architecture/004-api-design.md` exists and returns correct responses.

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

Segregation of duties: the responder MUST NOT be the user who triggered the request (`triggered_by != approved_by`). Enforced in handler — returns 403 if violated.

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

### 4.14 Executions (`/api/v1/projects/{projectId}/executions`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `DispatchTasks.cs` | POST | `/dispatch` | Operator+ |
| `RunAdHoc.cs` | POST | `/run` | Operator+ |
| `GetExecution.cs` | GET | `/{executionId}` | Viewer+ |

Note: `DispatchTasks` and `RunAdHoc` create records and enqueue to Redis. Actual execution is Phase 6+.

### 4.15 Catalog — Browse (`/api/v1/catalog`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListCatalog.cs` | GET | `/` | Member |
| `GetCatalogAgent.cs` | GET | `/{agentId}` | Member |

### 4.16 Admin: Dashboard (`/api/v1/admin/dashboard`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `GetHealth.cs` | GET | `/health` | PlatformAdmin |
| `GetOverview.cs` | GET | `/overview` | PlatformAdmin |
| `GetAdminCosts.cs` | GET | `/costs` | PlatformAdmin |
| `GetAdminCostTimeline.cs` | GET | `/costs/timeline` | PlatformAdmin |

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

### 4.18 Admin: Platform Knowledge (`/api/v1/admin/knowledge`)

| File | Method | Path | Auth |
|------|--------|------|------|
| `ListPlatformLearnings.cs` | GET | `/learnings` | PlatformAdmin |
| `ListPendingPromotions.cs` | GET | `/promotions/pending` | PlatformAdmin |
| `ApprovePromotion.cs` | POST | `/promotions/{promotionId}/approve` | PlatformAdmin |
| `RejectPromotion.cs` | POST | `/promotions/{promotionId}/reject` | PlatformAdmin |
| `EditPlatformLearning.cs` | PATCH | `/learnings/{learningId}` | PlatformAdmin |
| `RetractPlatformLearning.cs` | POST | `/learnings/{learningId}/retract` | PlatformAdmin |

---

## Supporting Infrastructure

### Additional Repository Methods

Some endpoints need query capabilities not yet in repositories:

```csharp
// IWorkflowRepository (extend from Phase 2)
Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct);
Task<IReadOnlyList<WorkflowDefinition>> ListByProjectAsync(Guid projectId, CancellationToken ct);
Task<WorkflowDefinition> CreateAsync(WorkflowDefinition def, CancellationToken ct);
Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition def, CancellationToken ct);
Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(Guid projectId, Guid? workflowId, CancellationToken ct);
Task<WorkflowRun?> GetRunByIdAsync(Guid runId, CancellationToken ct);
Task<IReadOnlyList<WorkflowSchedule>> ListSchedulesAsync(Guid projectId, CancellationToken ct);
Task<IReadOnlyList<HumanInputRequest>> ListPendingInputsAsync(Guid projectId, CancellationToken ct);
```

Note: Knowledge, decision, milestone, and artifact queries are simple reads — vertical-slice endpoints use `AppDbContext` directly (no repository abstraction needed for read-only paginated queries).

```csharp
// IDocumentRepository (new)
public interface IDocumentRepository
{
    Task<ProjectDocument> UploadAsync(ProjectDocument doc, CancellationToken ct);
    Task<IReadOnlyList<ProjectDocument>> ListAsync(Guid projectId, CancellationToken ct);
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<DocumentChunk>> SearchChunksAsync(Guid projectId, float[] queryEmbedding, int limit, CancellationToken ct);
}
```

```csharp
// ICostQueryService (new — read-only aggregation)
public interface ICostQueryService
{
    Task<CostSummary> GetSummaryAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<CostTimelineEntry>> GetTimelineAsync(Guid projectId, DateTime from, DateTime to, CancellationToken ct);
    Task<TokenEconomics> GetTokenEconomicsAsync(Guid projectId, CancellationToken ct);
}

public record CostSummary(decimal TotalUsd, IReadOnlyList<AgentCostBreakdown> ByAgent, IReadOnlyList<ModelCostBreakdown> ByModel);
public record CostTimelineEntry(DateTime Hour, decimal CostUsd, int Calls);
public record TokenEconomics(long TotalInput, long TotalOutput, long CacheRead, long CacheCreation, double CacheHitRate);
```

### PCD Service

The PCD (Project Context Document) is a JSON blob with structured paths. Operations on it need a service:

```csharp
public interface IProjectContextService
{
    Task<ProjectContext> GetAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<ContextChange>> GetHistoryAsync(Guid projectId, CancellationToken ct);
    Task AddPrincipleAsync(Guid projectId, string principle, string addedBy, CancellationToken ct);
    Task AddGuardrailAsync(Guid projectId, string guardrail, string addedBy, CancellationToken ct);
    Task RemovePrincipleAsync(Guid projectId, string principleId, CancellationToken ct);
    Task RemoveGuardrailAsync(Guid projectId, string guardrailId, CancellationToken ct);
}
```

### Workflow Graph Validation

```csharp
public interface IWorkflowValidator
{
    ValidationResult Validate(string nodesJson, string edgesJson);
}

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
```

Validates:
- Exactly one Start node, at least one End node
- All edges connect existing nodes
- No orphan nodes (unreachable from Start)
- Decision nodes have labeled outgoing edges
- No cycles (DAG check)

---

## File Summary

### Files to CREATE: ~85 endpoint files + 6 service/repository files

```
src/AgenticWorkforce.Api/Features/Workflows/ (9 files)
src/AgenticWorkforce.Api/Features/WorkflowRuns/ (2 files)
src/AgenticWorkforce.Api/Features/Schedules/ (5 files)
src/AgenticWorkforce.Api/Features/HumanInput/ (3 files)
src/AgenticWorkforce.Api/Features/Context/ (6 files)
src/AgenticWorkforce.Api/Features/Learnings/ (8 files)
src/AgenticWorkforce.Api/Features/Milestones/ (3 files)
src/AgenticWorkforce.Api/Features/Decisions/ (3 files)
src/AgenticWorkforce.Api/Features/Intent/ (3 files)
src/AgenticWorkforce.Api/Features/Artifacts/ (4 files)
src/AgenticWorkforce.Api/Features/Documents/ (6 files)
src/AgenticWorkforce.Api/Features/Events/ (1 file)
src/AgenticWorkforce.Api/Features/Costs/ (3 files)
src/AgenticWorkforce.Api/Features/Executions/ (3 files)
src/AgenticWorkforce.Api/Features/Catalog/ (2 files)
src/AgenticWorkforce.Api/Features/Admin/Dashboard/ (4 files)
src/AgenticWorkforce.Api/Features/Admin/Catalog/ (10 files)
src/AgenticWorkforce.Api/Features/Admin/Knowledge/ (6 files)
src/AgenticWorkforce.Domain/Interfaces/Services/IProjectContextService.cs
src/AgenticWorkforce.Domain/Interfaces/Services/ICostQueryService.cs
src/AgenticWorkforce.Domain/Interfaces/Services/IWorkflowValidator.cs
src/AgenticWorkforce.Domain/Interfaces/Repositories/IDocumentRepository.cs
src/AgenticWorkforce.Infrastructure/Repositories/DocumentRepository.cs
src/AgenticWorkforce.Infrastructure/Services/ProjectContextService.cs
src/AgenticWorkforce.Infrastructure/Services/CostQueryService.cs
src/AgenticWorkforce.Infrastructure/Services/WorkflowValidator.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Workflows/WorkflowCrudTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Admin/AdminAuthorizationTests.cs
```

---

## Key Implementation Notes

### Document Upload

`UploadDocument.cs` accepts `multipart/form-data`. Files are persisted via `IDocumentStore` (defined in Domain, implemented in Infrastructure).

**`IDocumentStore` is defined in Domain** (Phase 1 creates the interface):

```csharp
// Already defined in Phase 1:
// src/AgenticWorkforce.Domain/Interfaces/Services/IDocumentStore.cs
public interface IDocumentStore
{
    Task<string> UploadAsync(Guid projectId, string fileName, Stream content, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string storageUrl, CancellationToken ct);
    Task DeleteAsync(string storageUrl, CancellationToken ct);
}
```

**Phase 4 implements `LocalFileDocumentStore`** for dev (Azure Blob in Phase 11):

```csharp
// src/AgenticWorkforce.Infrastructure/Services/LocalFileDocumentStore.cs
internal sealed class LocalFileDocumentStore(ILogger<LocalFileDocumentStore> logger) : IDocumentStore
{
    private static readonly string BasePath = Path.Combine("var", "uploads");

    public async Task<string> UploadAsync(Guid projectId, string fileName, Stream content, string contentType, CancellationToken ct)
    {
        var dir = Path.Combine(BasePath, projectId.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid()}_{fileName}");
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        logger.LogDebug("Stored document at {Path}", path);
        return path; // storageUrl — becomes blob URI in production
    }
    // ... Download, Delete
}
```

Endpoint:

```csharp
app.MapPost("/api/v1/projects/{projectId}/documents", HandleAsync)
    .RequireAuthorization(Policies.RequireOperator)
    .DisableAntiforgery()
    .WithTags("Documents");
```

### Semantic Search (Learnings + Documents)

`SearchLearnings.cs` and `SearchDocuments.cs` accept a query string, call an embedding service (stubbed in this phase — returns zero vector), and query pgvector.

**`IEmbeddingService` is defined ONCE in Domain** (not duplicated in Api or Agents):

```csharp
// src/AgenticWorkforce.Domain/Interfaces/Services/IEmbeddingService.cs
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
```

**Single stub in Infrastructure** (used by both Api endpoints AND Agents context assembly in Phase 6):

```csharp
// src/AgenticWorkforce.Infrastructure/Services/StubEmbeddingService.cs
internal sealed class StubEmbeddingService : IEmbeddingService
{
    public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        => Task.FromResult(new float[1536]);

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
        => Task.FromResult(texts.Select(_ => new float[1536]).ToArray());
}
```

Registered once in `InfrastructureServiceExtensions.AddInfrastructure()`. Phase 6's context assembly and Phase 4's search endpoints both resolve the same `IEmbeddingService` from DI. When Azure OpenAI embeddings are connected (Phase 11), only the single implementation is swapped.

### DispatchTasks / RunAdHoc (Stub)

These endpoints create the execution record and enqueue to Redis Stream. The actual worker pickup happens in Phase 8. For now:

```csharp
// DispatchTasks.cs
// 1. Get all approved tasks for the project
// 2. Transition them to Queued
// 3. Create a WorkflowRun record
// 4. Publish event (stub — Phase 5 adds real pub/sub)
// 5. Return the run ID
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` — all tests pass
3. Swagger UI shows all ~120 endpoints grouped by tags
4. Admin endpoints return 403 for non-admin users
5. PCD endpoints correctly mutate and version the JSON context
6. Workflow validation catches invalid graphs (no start node, cycles, orphans)
7. Document upload accepts multipart and stores metadata
8. Cost endpoints aggregate from LlmCall table correctly
9. All endpoints follow vertical-slice pattern
10. No circular dependencies between feature folders

---

## Goal Command

```
/goal Extended API slices complete: Workflows (9), WorkflowRuns (2), Schedules (5), HumanInput (3), Context/PCD (6), Learnings (8), Milestones (3), Decisions (3), Intent (3), Artifacts (4), Documents (6), Events (1), Costs (3), Executions (3), Catalog (2), Admin Dashboard (4), Admin Catalog (10), Admin Knowledge (6). All use vertical-slice minimal API pattern with proper auth policies. Workflow validator checks graph integrity. PCD service manages JSON mutations with versioned history. Verify: dotnet build exits 0, dotnet test exits 0, Swagger shows all endpoints. Stop after 50 turns.
```
