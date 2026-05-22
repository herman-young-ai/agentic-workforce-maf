# Phase 3.5: Boundary Remediation

**Status:** Not Started
**Depends On:** Phase 3 (API Vertical Slices Core)
**Verification:** `dotnet build` and `dotnet test` exit 0; `scripts/check-rules.sh` passes DL-001; CQI does not regress.

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from Phase 3. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify Phase 3's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0
4. **Check `.githooks/pre-commit` for the active CQI floor.** Per [000-phase-overview.md](000-phase-overview.md#L14) the floor ramps to 65 after Phase 3. Current CQI is 60.3 — if the ramp is already active, raise it or pause it for the duration of this phase. Do not bypass the hook.

---

## Why This Phase Exists

Phase 3 shipped 32 endpoint files plus `ProjectAuthorizationService` that inject `AppDbContext` directly into Api handlers. This was a documented choice: [Phase 3 plan §Architecture Pattern, line 43](003-phase-3-api-vertical-slices-core.md#L43) says *"uses query repositories for non-trivial reads and `AppDbContext` directly for writes (no service layer, no CRUD repository shims)"*, and the existing repository XML comments codify it (e.g. [IProjectRepository.cs](../../src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectRepository.cs): *"Writes go through `AppDbContext.Projects.Add/Update` + `SaveChangesAsync` directly from vertical-slice handlers — wrapping those one-liners adds no value"*).

That choice contradicts three load-bearing rules:

- **Rule DL-001** ([004-rules/004_data_layer.jsonl](../004-rules/004_data_layer.jsonl)) — *"No DbContext usage outside Infrastructure project"* (severity: **error**)
- **Principle 4 — Wrap the Core** ([003-principles/001-architectural-principles.md](../003-principles/001-architectural-principles.md#L142-L176)) — *"Application code (Api, Worker) never imports external library namespaces directly. Only wrapper projects (Agents, Infrastructure) know about MAF, EF Core, Azure SDKs."*
- **C# coding standard** ([005-standards/01-csharp-coding-standards.md](../005-standards/01-csharp-coding-standards.md#L80)) — *"All EF Core code in `AgenticWorkforce.Infrastructure` — never in Domain or Api."*

The decisive long-term reason to honour the rules: **Worker and Agents will already need typed repository methods** for the `project.*` platform tools listed in [Principle 22](../003-principles/001-architectural-principles.md#L558). If the Api duplicates that query logic inline, "how we list approved tasks for a project" exists in two places — once in `ITaskRepository` (used by Worker/Agents) and once in `ListTasks.cs` (used by Api). When query semantics change, both must move in lockstep. That duplication is exactly what Principle 16 (single source of truth) forbids.

Phase 4 would add ~85 more endpoint files following the Phase 3 pattern, baking the violation across the whole API surface. This phase reverses the Phase 3 decision before Phase 4 lands.

---

## Objective

1. Refactor Phase 3's 32 violating endpoint files plus `ProjectAuthorizationService` to remove all `AppDbContext` injection from the Api project.
2. Add 6 new repository interfaces and implementations to cover DbSets currently accessed inline.
3. Apply schema retrofits required by Phase 4 (segregation of duties FK, promotion approval state).
4. Land 5 companion fixes that would amplify under Phase 4's expanded surface (idempotency cross-user replay, `Guid.Empty` silent fallback, client-side pagination, `RoleRank` brittleness, repository comment cleanup).

After this phase: only `Program.cs` (for migration on dev startup) and `DatabaseHealthCheck` retain `AppDbContext` references in the Api project. All other access flows through `AgenticWorkforce.Domain.Interfaces.Repositories.*`.

---

## Schema Retrofits

Two Phase 4 features (§4.4 Human Input segregation of duties, §4.18 Knowledge promotion approval) are not wireable against the current schema. Apply as a single migration before refactoring slices.

### `WorkflowRun.TriggeredById`

Today `WorkflowRun.TriggeredBy` is a `string?` ([codemap line 1015](../../.codemap/map.md#L1015)) — likely a display name or email. Phase 4 §4.4 must enforce `triggered_by != approved_by` with typed user identity, not stringly-typed display values.

| Field | Type | Notes |
|---|---|---|
| `TriggeredById` | `Guid?` | FK → `User.Id`; nullable because workflow runs can be triggered by schedules with no owning user |

Existing `TriggeredBy` (string) is retained for human-readable provenance (e.g. "schedule-cron-0942", "agent: workflow.designer").

### `ProjectLearning` promotion approval state

Today `ProjectLearning.PlatformPromoted` is a single bool ([codemap line 876](../../.codemap/map.md#L876)). Phase 4 §4.6 introduces `PromoteLearning` (Operator+) and §4.18 introduces `ApprovePromotion` / `RejectPromotion` (PlatformAdmin) — implying a pending state between submission and approval. The bool cannot represent that.

New enum and fields:

```csharp
// Domain/Enums.cs
public enum PromotionStatus
{
    None,
    PendingApproval,
    Approved,
    Rejected
}
```

| Field | Type | Replaces |
|---|---|---|
| `ProjectLearning.PromotionStatus` | `PromotionStatus` | (new) |
| `ProjectLearning.PromotionRequestedAt` | `DateTime?` | (new) |
| `ProjectLearning.PromotionRequestedById` | `Guid?` (FK → `User.Id`) | (new) |
| `ProjectLearning.PromotionRejectedReason` | `string?` | (new) |
| `ProjectLearning.PlatformPromoted` | `bool` | derived from `PromotionStatus == Approved`; either drop the column and re-derive, or update via the migration |

Decision: **drop `PlatformPromoted`**. `PromotionStatus == PromotionStatus.Approved` is the single source of truth. Existing `PromotedBy`, `PromotedAt` are kept; semantics narrow to "PlatformAdmin who approved".

Migration name: `AddSodAndPromotionStateRetrofits`. Single migration covering both retrofits to avoid two cascading EF Core model snapshots.

---

## Repository Additions

### Expand existing repositories

Replace the deliberately-narrow query-only surface with the methods Phase 3 slices currently inline.

```csharp
// IProjectRepository
Task<Project> AddAsync(Project project, CancellationToken ct);
Task UpdateAsync(Project project, CancellationToken ct);
Task<PagedResult<Project>> ListByMemberPagedAsync(
    Guid userId, ProjectStatus? statusFilter, PagedQuery paging, CancellationToken ct);

// ITaskRepository
Task<AgenticTask> AddAsync(AgenticTask task, CancellationToken ct);
Task UpdateAsync(AgenticTask task, CancellationToken ct);
Task<PagedResult<AgenticTask>> ListByProjectPagedAsync(
    Guid projectId, TaskListFilter filter, PagedQuery paging, CancellationToken ct);
Task<BulkApproveResult> BulkApproveAsync(
    Guid projectId, IReadOnlyList<Guid> taskIds, Guid approverId, CancellationToken ct);

// ISessionRepository
Task<Session> AddAsync(Session session, CancellationToken ct);
Task UpdateAsync(Session session, CancellationToken ct);
Task<PagedResult<Session>> ListByProjectPagedAsync(
    Guid projectId, SessionStatus? statusFilter, PagedQuery paging, CancellationToken ct);
Task<PagedResult<SessionMessage>> ListMessagesPagedAsync(
    Guid sessionId, PagedQuery paging, CancellationToken ct);
```

`TaskListFilter`, `BulkApproveResult` are simple record types declared alongside the interface.

### New repositories

| Interface | DbSets it owns | Methods (minimum viable) |
|---|---|---|
| `IUserRepository` | `Users` | `GetByIdAsync`, `UpdateAsync`, `EnsureProvisionedAsync(CurrentUser)` (JIT on first login) |
| `IApiKeyRepository` | `ApiKeys` | `AddAsync`, `ListByUserAsync`, `RevokeAsync(keyId, userId)` (soft delete via `RevokedAt`) |
| `IProjectMemberRepository` | `ProjectMembers` | `GetMembershipAsync(userId, projectId)`, `ListByProjectAsync`, `AddAsync`, `UpdateAsync`, `RemoveAsync`, `TransferOwnershipAsync(projectId, fromUserId, toUserId)` (single transaction) |
| `IProjectAgentRepository` | `ProjectAgents` | `ListByProjectAsync`, `AddAsync`, `RemoveAsync`, `SeedFromCatalogAsync(projectId, templateName, addedById)` |
| `IAgentCatalogRepository` | `AgentCatalogs` | `GetByIdAsync`, `GetByNameAsync`, `ListEnabledAsync` |
| `IPromptVersionRepository` | `PromptVersions` | `AddAsync`, `ListByEntityAsync(entityType, entityId)` |

Interfaces live in `AgenticWorkforce.Domain.Interfaces.Repositories`. Implementations live in `AgenticWorkforce.Infrastructure.Repositories`. Registration goes into `InfrastructureServiceExtensions.AddInfrastructure(...)`.

---

## Endpoint Refactor List

Every file below loses its `AppDbContext db` parameter, injects the appropriate repository instead, and replaces inline EF Core LINQ with repository methods. The refactor is behaviour-preserving — Phase 3 integration tests must pass without edits.

**Auth (5):** `CreateApiKey.cs`, `GetMe.cs`, `ListApiKeys.cs`, `RevokeApiKey.cs`, `UpdateMe.cs`

**Projects (8):** `ArchiveProject.cs`, `CreateProject.cs`, `DeleteProject.cs`, `GetProject.cs`, `PauseProject.cs`, `ResumeProject.cs`, `UpdateProject.cs`, `ListProjects.cs` *(also remediates client-side pagination — switch to `ListByMemberPagedAsync`)*

**Tasks (9):** `ApproveTask.cs`, `BulkApproveTask.cs` *(also switch to `BulkApproveAsync` single-transaction, eliminating per-task `SaveChanges` race)*, `CancelTask.cs`, `CreateTask.cs`, `ListTasks.cs` *(server-side pagination)*, `RejectTask.cs`, `RetryTask.cs`, `UpdateTask.cs`, `GetTask.cs` *(verify already through repo — no-op if so)*

**Sessions (6):** `CompleteSession.cs`, `CreateSession.cs`, `ListMessages.cs` *(server-side pagination)*, `ListSessions.cs` *(server-side pagination)*, `ResumeSession.cs`, `SuspendSession.cs`

**Members (5):** `AddMember.cs`, `ListMembers.cs`, `RemoveMember.cs`, `TransferOwnership.cs` *(use `TransferOwnershipAsync` single transaction)*, `UpdateMember.cs`

**Team (5):** `AddAgent.cs`, `ListTeam.cs`, `RemoveAgent.cs`, `SeedTeam.cs`, `UpdateAgentPrompt.cs`

**Core (1):** `ProjectAuthorizationService.cs` — inject `IProjectMemberRepository` instead of `AppDbContext`. Per-request `IMemoryCache` of membership lookups (5-min TTL, invalidated on `IProjectMemberRepository` mutations via an internal `IMembershipChangeNotifier`).

Total: **39 files modified, 6 new repository interfaces + 6 implementations created.**

---

## Companion Bugfixes

These fixes share the same surface as the refactor — bundle them rather than scheduling separate phases.

### 1. Idempotency user-scoping

`IIdempotencyService` signature changes to require explicit user identity:

```csharp
public interface IIdempotencyService
{
    Task<T?> GetCachedResponseAsync<T>(Guid userId, string key, CancellationToken ct = default);
    Task CacheResponseAsync<T>(Guid userId, string key, T response, CancellationToken ct = default);
}
```

Internal cache key composed as `$"{userId}:{key}"`. Callers must pass `currentUser.Id` explicitly — no implicit context lookup, no global key namespace. Fixes the cross-user replay vulnerability where a user could submit another user's idempotency key and receive the cached response.

Affected callers: `CreateProject`, `CreateTask`, `CreateSession`, `AddAgent`, `AddMember`.

### 2. `CurrentUser.ResolveObjectId` fail-fast

[CurrentUser.cs:33-42](../../src/AgenticWorkforce.Api/Core/Auth/CurrentUser.cs#L33-L42) throws `TokenInvalidException("Token has no parseable object identifier claim")` when no `oid` / `objectidentifier` / `uid` / `NameIdentifier` claim is present or parseable as `Guid`. Returns no fallback. Email and DisplayName likewise throw if all candidate claims are missing — a token without identity claims is not authenticated, regardless of `IsAuthenticated`.

### 3. Server-side pagination

`ListProjects`, `ListTasks`, `ListSessions`, `ListMessages`, `ListMembers`, `ListTeam` all switch to the new `*Paged` repository methods. `Skip`, `Take`, `Count` push to SQL. Eliminates the case where a user with 10k records pays for 10k rows on every page request.

### 4. `ProjectRole` renumber

[Enums.cs:5](../../src/AgenticWorkforce.Domain/Entities/Enums.cs#L5) currently declares `ProjectRole { Owner=0, Operator=1, Reviewer=2, Viewer=3 }` — integer order inverted from seniority. The `RoleRank` switch in [ProjectAuthorizationService.cs:38-45](../../src/AgenticWorkforce.Api/Core/Auth/ProjectAuthorizationService.cs#L38-L45) is the only correct comparator and is brittle.

Renumber:

```csharp
public enum ProjectRole
{
    Viewer   = 10,
    Operator = 20,
    Reviewer = 30,
    Owner    = 40
}
```

Delete the `RoleRank` switch. Comparison becomes a direct `member.Role >= minimumRole`. Add a migration step to rewrite stored enum values (column is `text` per `.HasConversion<string>()`, so the migration is `UPDATE project_members SET role = role` — column values are already string-encoded, but verify in case stored as integer historically).

### 5. Repository comment cleanup

Remove the "Writes go through `AppDbContext` directly from vertical-slice handlers — wrapping those one-liners adds no value and fragments the unit of work across repositories" comments from:

- `IProjectRepository.cs`
- `ITaskRepository.cs`
- `ISessionRepository.cs`
- `IWorkflowRepository.cs`

Replace with a one-line summary of the aggregate's purpose. These comments documented the obsoleted Phase 3 intent.

---

## File Summary

### Files to MODIFY

- `src/AgenticWorkforce.Domain/Entities/Enums.cs` (1) — add `PromotionStatus`; renumber `ProjectRole`
- `src/AgenticWorkforce.Domain/Entities/WorkflowRun.cs` (1) — add `TriggeredById`
- `src/AgenticWorkforce.Domain/Entities/ProjectLearning.cs` (1) — add `PromotionStatus`, `PromotionRequestedAt`, `PromotionRequestedById`, `PromotionRejectedReason`; remove `PlatformPromoted`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectRepository.cs` (1)
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/ITaskRepository.cs` (1)
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/ISessionRepository.cs` (1)
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IWorkflowRepository.cs` (1) — drop the "writes go through DbContext" comment
- `src/AgenticWorkforce.Infrastructure/Repositories/ProjectRepository.cs` (1)
- `src/AgenticWorkforce.Infrastructure/Repositories/TaskRepository.cs` (1)
- `src/AgenticWorkforce.Infrastructure/Repositories/SessionRepository.cs` (1)
- `src/AgenticWorkforce.Infrastructure/Data/Configurations/KnowledgeConfigurations.cs` (1) — `PromotionStatus` enum mapping
- `src/AgenticWorkforce.Infrastructure/Data/Configurations/WorkflowConfigurations.cs` (1) — `TriggeredById` FK
- `src/AgenticWorkforce.Infrastructure/DependencyInjection.cs` (1) — register new repositories
- `src/AgenticWorkforce.Api/Core/Auth/CurrentUser.cs` (1) — fail-fast
- `src/AgenticWorkforce.Api/Core/Auth/IdempotencyService.cs` (1) — user-scoped signature
- `src/AgenticWorkforce.Api/Core/Auth/ProjectAuthorizationService.cs` (1) — repository injection + `RoleRank` deletion
- 32 endpoint files in `src/AgenticWorkforce.Api/Features/` (listed in Endpoint Refactor List above)

Total: **48 files modified.**

### Files to CREATE

- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IUserRepository.cs`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IApiKeyRepository.cs`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectMemberRepository.cs`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IProjectAgentRepository.cs`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IAgentCatalogRepository.cs`
- `src/AgenticWorkforce.Domain/Interfaces/Repositories/IPromptVersionRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/UserRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/ApiKeyRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/ProjectMemberRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/ProjectAgentRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/AgentCatalogRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Repositories/PromptVersionRepository.cs`
- `src/AgenticWorkforce.Infrastructure/Migrations/{Timestamp}_AddSodAndPromotionStateRetrofits.cs`

Total: **13 files created.**

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` exits 0 — **no Phase 3 test files edited** (refactor must be behaviour-preserving)
3. `grep -rn 'AppDbContext' src/AgenticWorkforce.Api/ --include='*.cs' | grep -v 'Program.cs' | grep -v 'HealthCheck'` returns **zero results**
4. `grep -rn 'using Microsoft\.EntityFrameworkCore' src/AgenticWorkforce.Api/ --include='*.cs' | grep -v 'Program.cs' | grep -v 'HealthCheck'` returns **zero results**
5. `scripts/check-rules.sh` passes DL-001, DL-002, MB-001 through MB-005
6. CQI score does not regress below the active floor in `.githooks/pre-commit`
7. Idempotency tests verify cross-user keys are isolated (User A's `foo` is not visible to User B's `foo`)
8. `CurrentUser` test asserts `TokenInvalidException` when no `oid`-equivalent claim is present
9. Migration `AddSodAndPromotionStateRetrofits` applies cleanly to a fresh DB and to a Phase-3-populated DB (idempotent re-apply test)

---

## Goal Command

```
/goal Phase 3.5 boundary remediation complete: 32 Phase 3 endpoint files + ProjectAuthorizationService refactored to remove AppDbContext injection, 6 new repository interfaces + implementations added, schema retrofits (WorkflowRun.TriggeredById FK + PromotionStatus enum on ProjectLearning) applied via single migration AddSodAndPromotionStateRetrofits, ProjectRole renumbered and RoleRank deleted, IIdempotencyService signature changed to require user ID, CurrentUser.ResolveObjectId fails fast on missing oid claim, all List* endpoints use server-side pagination, repository XML comments cleaned. Verify: dotnet build + test exit 0 with no test edits, DL-001 grep returns zero, CQI no regression. Stop after 35 turns.
```

---

## Notes for Future Phases

This phase obsoletes the §Architecture Pattern note in [Phase 3 plan, line 43](003-phase-3-api-vertical-slices-core.md#L43). Future phase plans must use the repository-only pattern. The standing rule is: **`AppDbContext` is referenced only by `Program.cs` (dev-startup migration) and `DatabaseHealthCheck`. Every other access flows through `AgenticWorkforce.Domain.Interfaces.Repositories.*`.**
