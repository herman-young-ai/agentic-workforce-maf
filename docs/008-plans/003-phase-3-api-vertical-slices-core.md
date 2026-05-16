# Phase 3: API Vertical Slices (Core)

**Status:** Not Started
**Depends On:** Phase 2 (Data Layer)
**Verification:** `dotnet test AgenticWorkforce.slnx` passes — all endpoints return correct status codes with real PostgreSQL

---

## Objective

Implement the core CRUD API endpoints using the vertical-slice pattern. Each feature is self-contained: endpoint + handler + request/response DTOs in a single file. This phase delivers the essential project management API that the frontend and agents need.

---

## Architecture Pattern

Per AGENTS.md, this project uses **vertical slices, NOT classic layering**:

```
Api/Features/{Resource}/
├── Create{Resource}.cs      (endpoint + handler + request/response DTOs)
├── Get{Resource}.cs
├── List{Resources}.cs
├── Update{Resource}.cs
└── Delete{Resource}.cs
```

Each file contains:
1. A static class with a `MapEndpoints(IEndpointRouteBuilder)` method
2. Request/Response record types
3. The handler logic (calling repository directly — no service layer for CRUD)

### Endpoint Registration Pattern

```csharp
// In each feature file:
public static class CreateProject
{
    public record Request(string Name, string Objective, string? Description, ProjectTier Tier = ProjectTier.User);
    public record Response(Guid Id, string Name, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/projects", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Projects");
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        ICurrentUserAccessor user,
        IProjectRepository repo,
        CancellationToken ct)
    {
        // handler logic
    }
}

// In Program.cs (or a registration extension):
app.MapProjectEndpoints();
app.MapTaskEndpoints();
// etc.
```

### OpenAPI for Minimal APIs

Replace `Swashbuckle.AspNetCore` with .NET 10's built-in `Microsoft.AspNetCore.OpenApi` which natively discovers minimal API endpoints:

```csharp
// Program.cs — replace Swashbuckle registration with:
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info.Title = "Agentic Workforce API";
        doc.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

// Replace app.UseSwagger()/UseSwaggerUI() with:
app.MapOpenApi(); // serves /openapi/v1.json
app.UseSwaggerUI(opts => opts.SwaggerEndpoint("/openapi/v1.json", "v1")); // keep SwaggerUI for browsing
```

Remove `Swashbuckle.AspNetCore` from packages. Add `Microsoft.AspNetCore.OpenApi` (included in .NET 10 SDK — no extra package needed). Keep `Swashbuckle.AspNetCore.SwaggerUI` for the UI only if desired, or use Scalar.

### Cross-Cutting: Project-Scoped Authorization

Most endpoints are project-scoped. We need a lightweight authorization check that verifies the current user has the required role ON THAT PROJECT (BOLA prevention):

```csharp
// Api/Core/Auth/ProjectAuthorizationService.cs
public interface IProjectAuthorizationService
{
    Task<bool> HasRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
    Task EnsureRoleAsync(Guid userId, Guid projectId, ProjectRole minimumRole, CancellationToken ct = default);
}
```

`EnsureRoleAsync` throws `ForbiddenException` if the user lacks the role. Platform admins bypass project role checks.

---

## Endpoints by Feature

### 3.1 Projects (`/api/v1/projects`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `CreateProject.cs` | POST | `/` | Member | Idempotency-Key; creator becomes Owner |
| `GetProject.cs` | GET | `/{projectId}` | Viewer+ | Includes summary stats |
| `ListProjects.cs` | GET | `/` | Member | Filtered by membership |
| `UpdateProject.cs` | PATCH | `/{projectId}` | Owner | Partial update |
| `DeleteProject.cs` | DELETE | `/{projectId}` | Owner | Soft-delete (IsActive=false) |
| `PauseProject.cs` | POST | `/{projectId}/pause` | Owner | Status → Paused |
| `ResumeProject.cs` | POST | `/{projectId}/resume` | Owner | Status → Active |
| `ArchiveProject.cs` | POST | `/{projectId}/archive` | Owner | Status → Archived |

### 3.2 Tasks (`/api/v1/projects/{projectId}/tasks`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `ListTasks.cs` | GET | `/` | Viewer+ | Filters: status, type, source, agent_name, parent_task_id |
| `GetTask.cs` | GET | `/{taskId}` | Viewer+ | Expanded: attempts, artifacts, learnings |
| `CreateTask.cs` | POST | `/` | Operator+ | Status=Proposed; Idempotency-Key |
| `UpdateTask.cs` | PATCH | `/{taskId}` | Operator+ | Only while status=Proposed |
| `ApproveTask.cs` | POST | `/{taskId}/approve` | Reviewer+ | Segregation: created_by != approver |
| `RejectTask.cs` | POST | `/{taskId}/reject` | Reviewer+ | Requires `reason` |
| `RetryTask.cs` | POST | `/{taskId}/retry` | Operator+ | Failed → Approved |
| `CancelTask.cs` | POST | `/{taskId}/cancel` | Operator+ | |
| `GetBoard.cs` | GET | `/board` | Viewer+ | Grouped by status, includes deps |
| `BulkApproveTask.cs` | POST | `/bulk-approve` | Reviewer+ | Body: `{ task_ids[] }` |

### 3.3 Sessions (`/api/v1/projects/{projectId}/sessions`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `CreateSession.cs` | POST | `/` | Operator+ | Idempotency-Key |
| `GetSession.cs` | GET | `/{sessionId}` | Viewer+ | |
| `ListSessions.cs` | GET | `/` | Viewer+ | |
| `ListMessages.cs` | GET | `/{sessionId}/messages` | Viewer+ | Paginated |
| `SuspendSession.cs` | POST | `/{sessionId}/suspend` | Operator+ | Requires `reason` |
| `ResumeSession.cs` | POST | `/{sessionId}/resume` | Operator+ | |
| `CompleteSession.cs` | POST | `/{sessionId}/complete` | Operator+ | |

### 3.4 Members (`/api/v1/projects/{projectId}/members`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `ListMembers.cs` | GET | `/` | Viewer+ | |
| `AddMember.cs` | POST | `/` | Owner | Idempotency-Key |
| `UpdateMember.cs` | PATCH | `/{userId}` | Owner | Update role |
| `RemoveMember.cs` | DELETE | `/{userId}` | Owner | Cannot remove owner |
| `TransferOwnership.cs` | POST | `/transfer-ownership` | Owner | Body: `{ new_owner_id }` |

### 3.5 Team — Agents (`/api/v1/projects/{projectId}/team`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `ListTeam.cs` | GET | `/` | Viewer+ | |
| `AddAgent.cs` | POST | `/` | Owner | From catalog; Idempotency-Key |
| `RemoveAgent.cs` | DELETE | `/{memberId}` | Owner | |
| `UpdateAgentPrompt.cs` | PUT | `/{memberId}/prompt` | Operator+ | Versioned via PromptVersion |
| `SeedTeam.cs` | POST | `/seed` | Owner | Seeds all enabled catalog agents |

### 3.6 Auth (`/api/v1/auth`)

| File | Method | Path | Auth | Notes |
|------|--------|------|------|-------|
| `GetMe.cs` | GET | `/me` | Authenticated | |
| `UpdateMe.cs` | PATCH | `/me` | Authenticated | display_name only |
| `CreateApiKey.cs` | POST | `/me/api-keys` | Authenticated | Returns full key once |
| `ListApiKeys.cs` | GET | `/me/api-keys` | Authenticated | Masked |
| `RevokeApiKey.cs` | DELETE | `/me/api-keys/{keyId}` | Authenticated | |

---

## Supporting Infrastructure

### Files to CREATE

```
src/AgenticWorkforce.Api/Core/Auth/ProjectAuthorizationService.cs
src/AgenticWorkforce.Api/Core/Auth/IdempotencyMiddleware.cs
src/AgenticWorkforce.Api/Core/Extensions/EndpointRegistrationExtensions.cs
src/AgenticWorkforce.Api/Features/Projects/CreateProject.cs
src/AgenticWorkforce.Api/Features/Projects/GetProject.cs
src/AgenticWorkforce.Api/Features/Projects/ListProjects.cs
src/AgenticWorkforce.Api/Features/Projects/UpdateProject.cs
src/AgenticWorkforce.Api/Features/Projects/DeleteProject.cs
src/AgenticWorkforce.Api/Features/Projects/PauseProject.cs
src/AgenticWorkforce.Api/Features/Projects/ResumeProject.cs
src/AgenticWorkforce.Api/Features/Projects/ArchiveProject.cs
src/AgenticWorkforce.Api/Features/Tasks/ListTasks.cs
src/AgenticWorkforce.Api/Features/Tasks/GetTask.cs
src/AgenticWorkforce.Api/Features/Tasks/CreateTask.cs
src/AgenticWorkforce.Api/Features/Tasks/UpdateTask.cs
src/AgenticWorkforce.Api/Features/Tasks/ApproveTask.cs
src/AgenticWorkforce.Api/Features/Tasks/RejectTask.cs
src/AgenticWorkforce.Api/Features/Tasks/RetryTask.cs
src/AgenticWorkforce.Api/Features/Tasks/CancelTask.cs
src/AgenticWorkforce.Api/Features/Tasks/GetBoard.cs
src/AgenticWorkforce.Api/Features/Tasks/BulkApproveTask.cs
src/AgenticWorkforce.Api/Features/Sessions/CreateSession.cs
src/AgenticWorkforce.Api/Features/Sessions/GetSession.cs
src/AgenticWorkforce.Api/Features/Sessions/ListSessions.cs
src/AgenticWorkforce.Api/Features/Sessions/ListMessages.cs
src/AgenticWorkforce.Api/Features/Sessions/SuspendSession.cs
src/AgenticWorkforce.Api/Features/Sessions/ResumeSession.cs
src/AgenticWorkforce.Api/Features/Sessions/CompleteSession.cs
src/AgenticWorkforce.Api/Features/Members/ListMembers.cs
src/AgenticWorkforce.Api/Features/Members/AddMember.cs
src/AgenticWorkforce.Api/Features/Members/UpdateMember.cs
src/AgenticWorkforce.Api/Features/Members/RemoveMember.cs
src/AgenticWorkforce.Api/Features/Members/TransferOwnership.cs
src/AgenticWorkforce.Api/Features/Team/ListTeam.cs
src/AgenticWorkforce.Api/Features/Team/AddAgent.cs
src/AgenticWorkforce.Api/Features/Team/RemoveAgent.cs
src/AgenticWorkforce.Api/Features/Team/UpdateAgentPrompt.cs
src/AgenticWorkforce.Api/Features/Team/SeedTeam.cs
src/AgenticWorkforce.Api/Features/Auth/GetMe.cs
src/AgenticWorkforce.Api/Features/Auth/UpdateMe.cs
src/AgenticWorkforce.Api/Features/Auth/CreateApiKey.cs
src/AgenticWorkforce.Api/Features/Auth/ListApiKeys.cs
src/AgenticWorkforce.Api/Features/Auth/RevokeApiKey.cs
tests/AgenticWorkforce.Api.Tests.Unit/Features/Projects/CreateProjectTests.cs
tests/AgenticWorkforce.Api.Tests.Unit/Features/Tasks/ApproveTaskTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Projects/ProjectCrudTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Tasks/TaskLifecycleTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Auth/AuthorizationTests.cs
```

---

## Key Implementation Details

### Pagination

```csharp
// Api/Core/Pagination/PagedResult.cs (already exists, extend if needed)
public record PagedRequest(int Page = 1, int PageSize = 25);
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
```

All `List*` endpoints accept `[AsParameters] PagedRequest` and return `PagedResult<T>`.

### Idempotency Middleware

For POST endpoints that accept `X-Idempotency-Key`:
- Store `{key} → {response}` in Redis with 24h TTL
- If key exists, return cached response (201 or 200) without re-executing
- If key not present in request on required endpoints, return 400

```csharp
// Lightweight — not a full middleware, just a service called by endpoints that need it
public interface IIdempotencyService
{
    Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken ct = default);
    Task CacheResponseAsync<T>(string key, T response, CancellationToken ct = default);
}
```

For Phase 3, implement with in-memory dictionary (Redis integration comes in Phase 5). The interface is stable.

### Project-Scoped Authorization Flow

Every project-scoped endpoint follows this pattern:

```csharp
private static async Task<IResult> HandleAsync(
    Guid projectId,
    ICurrentUserAccessor userAccessor,
    IProjectAuthorizationService authz,
    IProjectRepository repo,
    CancellationToken ct)
{
    var user = userAccessor.User;
    await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

    var project = await repo.GetByIdAsync(projectId, ct)
        ?? throw new NotFoundException("Project", projectId);

    // ... business logic ...
}
```

### Segregation of Duties (Task Approval)

```csharp
// In ApproveTask.cs
var task = await taskRepo.GetByIdAsync(taskId, ct)
    ?? throw new NotFoundException("Task", taskId);

if (task.CreatedById == user.Id)
    throw new ForbiddenException("Segregation of duties: creator cannot approve their own task.");

task.Status = TaskStatus.Approved;
```

### State Transition Validation

Tasks have a defined state machine. Invalid transitions throw `InvalidStateException`:

```
Proposed → Approved (approve) | Cancelled (cancel)
Approved → Queued (dispatch) | Cancelled (cancel)
Queued → Running (agent picks up)
Running → Completed | Failed | Cancelled
Failed → Approved (retry)
```

---

## Program.cs Changes

Remove `builder.Services.AddControllers()` and `app.MapControllers()`. Replace with minimal API endpoint mapping:

```csharp
// Remove:
// builder.Services.AddControllers();
// app.MapControllers();

// Add (after UseAuthorization):
app.MapProjectEndpoints();
app.MapTaskEndpoints();
app.MapSessionEndpoints();
app.MapMemberEndpoints();
app.MapTeamEndpoints();
app.MapAuthEndpoints();
```

Each `Map*Endpoints()` is an extension method in `EndpointRegistrationExtensions.cs` that calls the individual feature's `MapEndpoints()`:

```csharp
public static class EndpointRegistrationExtensions
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        CreateProject.MapEndpoints(app);
        GetProject.MapEndpoints(app);
        ListProjects.MapEndpoints(app);
        UpdateProject.MapEndpoints(app);
        DeleteProject.MapEndpoints(app);
        PauseProject.MapEndpoints(app);
        ResumeProject.MapEndpoints(app);
        ArchiveProject.MapEndpoints(app);
    }
    // ... same pattern for Tasks, Sessions, etc.
}
```

---

## Test Strategy

### Unit Tests (Domain logic only)

- Task state transitions (valid and invalid)
- Segregation of duties check
- Authorization service role hierarchy

### Integration Tests (real PostgreSQL)

- Full CRUD lifecycle: create project → add members → create task → approve → verify state
- Authorization: verify each role can/cannot access endpoints
- Idempotency: send same request twice, get same response
- Pagination: create N entities, verify paging works
- BOLA: user A cannot access user B's project

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` — all tests pass
3. Swagger UI (`/swagger`) shows all endpoints grouped by tag
4. POST `/api/v1/projects` with valid auth returns 201
5. GET `/api/v1/projects/{id}` returns project with members
6. POST `/api/v1/projects/{id}/tasks/{taskId}/approve` enforces segregation of duties
7. Unauthorized user gets 403 on project they're not a member of
8. No `Controller` classes exist anywhere in the project
9. Every endpoint has `RequireAuthorization` with appropriate policy
10. All endpoints follow the vertical-slice pattern (handler + DTOs in single file)

---

## Goal Command

```
/goal Core API vertical slices complete: Projects (8 endpoints), Tasks (10 endpoints), Sessions (7 endpoints), Members (5 endpoints), Team (5 endpoints), Auth (5 endpoints) — all using minimal API with vertical-slice pattern. Project-scoped authorization prevents BOLA. Task state machine enforces valid transitions. Segregation of duties on approval. No Controller classes. Verify: dotnet build exits 0, dotnet test exits 0 with integration tests hitting real PostgreSQL via Testcontainers. Stop after 40 turns.
```
