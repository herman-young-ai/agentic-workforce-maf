# R16 Response: API Surface Design — Agentic Workforce Platform

**Generated:** 2026-05-11
**Input:** R16 prompt + prototype API surface (001b-api-surface-reference.md)

---

## 1. Endpoint Reference Tables

### 1.1 Health

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/alive` | Liveness (process up) | Public | Returns 200 OK |
| `GET` | `/health/ready` | Readiness (DB + Redis) | Public | 503 if unhealthy |
| `GET` | `/health/detailed` | Per-component status + latency | PlatformAdmin | Exposes infrastructure topology |

### 1.2 Auth (`/api/v1/auth`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/sse-token` | JWT → short-lived Redis SSE token (30s TTL) | Authenticated | Single-use via GETDEL; passed as `?token=` query param on SSE endpoints (EventSource API cannot set headers); access logs MUST redact query strings on `/stream` paths |
| `GET` | `/me` | Current user profile | Authenticated | |
| `PATCH` | `/me` | Update own profile (display_name) | Authenticated | |
| `PUT` | `/me/password` | Change own password | Authenticated | |
| `POST` | `/me/api-keys` | Create API key | Authenticated | Returns full key once |
| `GET` | `/me/api-keys` | List own API keys | Authenticated | Masked |
| `DELETE` | `/me/api-keys/{keyId}` | Revoke own API key | Authenticated | |

> No `/login` or `/refresh` — Entra ID handles token issuance externally. The platform maintains a local `User` table that maps to Entra ID Object IDs. `POST /admin/users` creates this platform record (auto-provisioned on first login via JIT). `reset-password` applies only to the API key credential, not Entra ID passwords.

### 1.3 Projects (`/api/v1/projects`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/` | Create project | Member | Idempotency-Key |
| `GET` | `/` | List projects | Member | Filtered by membership |
| `GET` | `/{projectId}` | Project detail + summary stats | Viewer+ | |
| `PATCH` | `/{projectId}` | Update project fields | Owner | |
| `DELETE` | `/{projectId}` | Delete project (cascading soft-delete) | Owner | |
| `GET` | `/{projectId}/overview` | Rich summary (activity, pending approvals, active sessions) | Viewer+ | |
| `POST` | `/{projectId}/pause` | Pause project (suspends tasks) | Owner | |
| `POST` | `/{projectId}/resume` | Resume paused project | Owner | |
| `POST` | `/{projectId}/archive` | Archive project | Owner | |
| `POST` | `/{projectId}/unarchive` | Restore archived project | Owner | |

### 1.4 Project Team — Agents (`/api/v1/projects/{projectId}/team`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List agent team members | Viewer+ | |
| `POST` | `/` | Add agent from catalog | Owner | Idempotency-Key |
| `DELETE` | `/{memberId}` | Remove agent from team | Owner | |
| `PUT` | `/{memberId}/prompt` | Update agent user prompt (versioned) | Operator+ | |
| `PATCH` | `/{memberId}/constraints` | Merge custom constraints | Owner | |
| `GET` | `/{memberId}/prompt-history` | User prompt version history | Viewer+ | |
| `POST` | `/seed` | Seed team from template | Owner | |

### 1.5 Project Members — Humans (`/api/v1/projects/{projectId}/members`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List human members | Viewer+ | |
| `POST` | `/` | Add member with role | Owner | Idempotency-Key |
| `PATCH` | `/{userId}` | Update member role | Owner | |
| `DELETE` | `/{userId}` | Remove member | Owner | Cannot remove owner |
| `POST` | `/transfer-ownership` | Transfer ownership to another member | Owner | Body: `{ new_owner_id }` |

### 1.6 Tasks (`/api/v1/projects/{projectId}/tasks`) — **The Primitive**

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List tasks (filterable) | Viewer+ | Filters: status, type, source, agent_id, parent_task_id |
| `GET` | `/{taskId}` | Task detail (attempts, artifacts, learnings, events) | Viewer+ | Expanded view |
| `POST` | `/` | Create task manually | Operator+ | Idempotency-Key; status=proposed |
| `PATCH` | `/{taskId}` | Update proposed task (title, description, inputs) | Operator+ | Only while status=proposed |
| `POST` | `/{taskId}/approve` | Approve proposed task + transitive deps | Reviewer+ | Segregation enforced |
| `POST` | `/{taskId}/reject` | Reject task + cancel dependents | Reviewer+ | Requires `reason` |
| `POST` | `/{taskId}/retry` | Retry failed task (→ approved) | Operator+ | |
| `POST` | `/{taskId}/cancel` | Cancel running task | Operator+ | |
| `GET` | `/{taskId}/cost` | Task cost breakdown (by model, attempt) | Viewer+ | |
| `GET` | `/{taskId}/dependencies` | Task dependency graph | Viewer+ | DAG structure |
| `POST` | `/bulk-approve` | Approve proposed tasks (skip-and-report) | Reviewer+ | Body: `BulkApproveRequest`; segregation skips reported |

### 1.7 Task Execution (`/api/v1/projects/{projectId}/executions`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/dispatch` | Dispatch all approved tasks | Operator+ | Returns `DispatchResponse` with run_id and task list |
| `POST` | `/run` | Ad-hoc: create + execute task in one call | Operator+ | Idempotency-Key; body: `RunAdHocRequest`; returns `RunAdHocResponse` |
| `GET` | `/{executionId}` | Execution status (polling fallback) | Viewer+ | Prefer SignalR |

### 1.8 Workflows (`/api/v1/projects/{projectId}/workflows`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List workflow definitions | Viewer+ | |
| `POST` | `/` | Create workflow definition | Operator+ | Idempotency-Key |
| `GET` | `/{workflowId}` | Workflow detail with nodes/edges | Viewer+ | |
| `PATCH` | `/{workflowId}` | Update workflow metadata | Operator+ | |
| `DELETE` | `/{workflowId}` | Delete workflow definition | Owner | |
| `POST` | `/import` | Import workflow from YAML | Operator+ | Idempotency-Key |
| `POST` | `/{workflowId}/validate` | Validate graph (dry run) | Operator+ | Returns validation errors |
| `PUT` | `/{workflowId}/canvas` | Save visual editor canvas state | Operator+ | JSON blob |
| `POST` | `/{workflowId}/run` | Run workflow (creates tasks, dispatches) | Operator+ | |
| `GET` | `/{workflowId}/runs` | List runs for this workflow | Viewer+ | |

### 1.9 Workflow Runs (`/api/v1/projects/{projectId}/workflow-runs`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List ALL workflow runs across all workflows | Viewer+ | Filters: workflow_id, status |
| `GET` | `/{runId}` | Workflow run detail | Viewer+ | Includes task summaries |

### 1.10 Workflow Schedules (`/api/v1/projects/{projectId}/schedules`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List schedules | Viewer+ | |
| `POST` | `/` | Create cron schedule | Owner | Idempotency-Key; body: `{ workflow_id, cron, timezone }` |
| `PATCH` | `/{scheduleId}` | Update schedule (incl. `enabled` field) | Owner | Use `{ "enabled": true/false }` to toggle |
| `DELETE` | `/{scheduleId}` | Delete schedule | Owner | |
| `GET` | `/upcoming` | List next N scheduled runs | Viewer+ | Query: `limit` |

### 1.11 Human Input / Approval Gates (`/api/v1/projects/{projectId}/human-input`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/pending` | List pending human input requests | Reviewer+ | |
| `POST` | `/{requestId}/respond` | Approve/reject/escalate with reason | Reviewer+ | Segregation: triggered_by != approved_by |
| `GET` | `/{requestId}/audit` | Approval audit trail for a request | Viewer+ | |

### 1.12 Project Context — PCD (`/api/v1/projects/{projectId}/context`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | Full PCD with version/size metrics | Viewer+ | |
| `GET` | `/history` | PCD change history | Viewer+ | |
| `POST` | `/principles` | Add principle | Operator+ | Idempotency-Key |
| `POST` | `/guardrails` | Add guardrail | Operator+ | Idempotency-Key |
| `DELETE` | `/principles/{principleId}` | Remove principle | Owner | |
| `DELETE` | `/guardrails/{guardrailId}` | Remove guardrail | Owner | |

### 1.13 Knowledge & Learnings (`/api/v1/projects/{projectId}/learnings`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List learnings (filterable) | Viewer+ | Filters: kind, status, confidence, agent_id, tags |
| `GET` | `/{learningId}` | Learning detail with evidence | Viewer+ | |
| `POST` | `/{learningId}/retract` | Retract learning (mark wrong) | Operator+ | Requires `reason` |
| `PATCH` | `/{learningId}` | Edit learning | Owner | |
| `POST` | `/{learningId}/supersede` | Create corrected version | Operator+ | Links to original |
| `POST` | `/{learningId}/promote` | Propose promotion to platform | Operator+ | |
| `POST` | `/search` | Semantic search (pgvector) | Viewer+ | Body: `{ query, limit }` |
| `GET` | `/{learningId}/similar` | Find similar (dedup check) | Viewer+ | Query: `limit`, `threshold` |

### 1.14 Decisions (`/api/v1/projects/{projectId}/decisions`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List decisions | Viewer+ | Filter: status (active, resolved) |
| `GET` | `/{decisionId}` | Decision detail | Viewer+ | |
| `POST` | `/` | Create decision manually | Operator+ | Idempotency-Key |

### 1.15 Intent (`/api/v1/projects/{projectId}/intent`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | Get current intent | Viewer+ | |
| `GET` | `/history` | Intent revision history | Viewer+ | |
| `POST` | `/` | Create new intent revision | Operator+ | Idempotency-Key; body: `{ content, scope, reason }` |

### 1.16 Artifacts (`/api/v1/projects/{projectId}/artifacts`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List artifacts | Viewer+ | Filters: type, format, agent_id |
| `GET` | `/{artifactId}` | Artifact detail (metadata) | Viewer+ | |
| `GET` | `/{artifactId}/content` | Inline render or download URL | Viewer+ | Content-Type varies |
| `POST` | `/{artifactId}/retract` | Soft-delete artifact (retract, not hard-delete) | Owner | Record retained for audit; content purged after retention period |

### 1.17 Documents (`/api/v1/projects/{projectId}/documents`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/` | Upload document | Operator+ | Idempotency-Key; multipart/form-data |
| `GET` | `/` | List documents with extraction status | Viewer+ | |
| `GET` | `/{documentId}` | Document detail (metadata + status) | Viewer+ | |
| `GET` | `/{documentId}/text` | Extracted text | Viewer+ | |
| `POST` | `/{documentId}/retract` | Soft-delete document (retract, not hard-delete) | Owner | Record retained for audit; content purged after retention period |
| `POST` | `/search` | Semantic search over documents | Viewer+ | Body: `{ query, limit }` |

### 1.18 Sessions & Chat (`/api/v1/projects/{projectId}/sessions`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/` | Create session | Operator+ | Idempotency-Key; optional `task_id` |
| `GET` | `/` | List sessions | Viewer+ | |
| `GET` | `/{sessionId}` | Session detail | Viewer+ | |
| `GET` | `/{sessionId}/messages` | Messages (paginated) | Viewer+ | |
| `POST` | `/{sessionId}/messages` | Send message → SSE agent response | Operator+ | `text/event-stream` |
| `POST` | `/{sessionId}/suspend` | Suspend session | Operator+ | Requires `reason` |
| `POST` | `/{sessionId}/resume` | Resume session | Operator+ | |
| `POST` | `/{sessionId}/complete` | Complete session | Operator+ | |

### 1.19 Console / Events (`/api/v1/projects/{projectId}/events`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | Event history (paginated, filterable) | Viewer+ | Filters: event_type, source, date range |
| `GET` | `/stream` | SSE: project events (real-time console) | SseAuth | |
| `GET` | `/stream/tasks/{taskId}` | SSE: task-scoped events | SseAuth | |

### 1.20 Notifications (`/api/v1/notifications`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/stream` | SSE: user notifications (all projects) | SseAuth | Scoped by user identity, not project |

### 1.21 Project Costs (`/api/v1/projects/{projectId}/costs`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/summary` | Total, by agent, by model | Viewer+ | |
| `GET` | `/timeline` | Hourly cost aggregation | Viewer+ | Query: `from`, `to` |
| `GET` | `/token-economics` | Input/output ratio, cache hit rate | Viewer+ | |

### 1.22 Project Search (`/api/v1/projects/{projectId}/search`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/` | Unified search (semantic + structured) | Viewer+ | Body: `{ query, mode, scope[], filters }` |

### 1.23 Agent Catalog — Browse (`/api/v1/catalog`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List enabled catalog agents | Member | Read-only browse for SPA/CLI |
| `GET` | `/{agentId}` | Agent detail (capabilities, description) | Member | No system prompt exposed |

### 1.24 Admin: Dashboard (`/api/v1/admin/dashboard`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/health` | Platform health (DB, Redis, Foundry) | PlatformAdmin | |
| `GET` | `/overview` | Active projects, running tasks, total cost | PlatformAdmin | |
| `GET` | `/costs` | Cost breakdown (model, project, agent) | PlatformAdmin | |
| `GET` | `/costs/timeline` | Hourly cost for charts | PlatformAdmin | |
| `GET` | `/quotas` | Model quota status | PlatformAdmin | |

### 1.25 Admin: Agent Catalog (`/api/v1/admin/catalog`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List all catalog entries (incl. disabled) | PlatformAdmin | |
| `POST` | `/` | Create catalog entry | PlatformAdmin | |
| `GET` | `/{agentId}` | Catalog entry detail | PlatformAdmin | |
| `PATCH` | `/{agentId}` | Update catalog entry | PlatformAdmin | |
| `DELETE` | `/{agentId}` | Delete catalog entry | PlatformAdmin | |
| `PUT` | `/{agentId}/prompt` | Update system prompt (versioned) | PlatformAdmin | |
| `GET` | `/{agentId}/prompt-history` | Prompt version history | PlatformAdmin | |
| `POST` | `/{agentId}/enable` | Enable agent | PlatformAdmin | |
| `POST` | `/{agentId}/disable` | Disable agent | PlatformAdmin | |
| `POST` | `/seed` | Seed catalog from YAML | PlatformAdmin | |
| `GET` | `/{agentId}/stats` | Usage stats (projects, cost, success rate) | PlatformAdmin | Filter by `agent_name` on list |

### 1.26 Admin: Platform Knowledge (`/api/v1/admin/knowledge`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/learnings` | Platform-promoted learnings | PlatformAdmin | |
| `GET` | `/promotions/pending` | Pending promotion requests | PlatformAdmin | |
| `POST` | `/promotions/{promotionId}/approve` | Approve promotion | PlatformAdmin | |
| `POST` | `/promotions/{promotionId}/reject` | Reject promotion | PlatformAdmin | |
| `PATCH` | `/learnings/{learningId}` | Edit platform learning | PlatformAdmin | |
| `POST` | `/learnings/{learningId}/retract` | Retract platform learning | PlatformAdmin | |
| `POST` | `/learnings/{learningId}/demote` | Remove from platform | PlatformAdmin | |

### 1.27 Admin: Users (`/api/v1/admin/users`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List users (paginated) | PlatformAdmin | |
| `POST` | `/` | Create user | PlatformAdmin | |
| `GET` | `/{userId}` | User detail | PlatformAdmin | |
| `PATCH` | `/{userId}` | Update user | PlatformAdmin | |
| `DELETE` | `/{userId}` | Deactivate user (soft delete) | PlatformAdmin | |
| `POST` | `/{userId}/reset-api-credential` | Reset API key credential | PlatformAdmin | Not Entra ID password — see Auth note |
| `POST` | `/{userId}/api-keys` | Create API key for user | PlatformAdmin | |
| `GET` | `/{userId}/api-keys` | List user's API keys | PlatformAdmin | |
| `DELETE` | `/{userId}/api-keys/{keyId}` | Revoke user's API key | PlatformAdmin | |
| `GET` | `/roles` | List configured roles | PlatformAdmin | |

### 1.28 Admin: Templates (`/api/v1/admin/templates`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `GET` | `/` | List project templates | PlatformAdmin | |
| `POST` | `/` | Create template | PlatformAdmin | |
| `GET` | `/{templateId}` | Template detail (composition, guardrails) | PlatformAdmin | |
| `PATCH` | `/{templateId}` | Update template | PlatformAdmin | |
| `DELETE` | `/{templateId}` | Delete template | PlatformAdmin | |

### 1.29 Admin: Emergency (`/api/v1/admin/emergency`)

| Method | Path | Purpose | Auth | Notes |
|--------|------|---------|------|-------|
| `POST` | `/stop` | Emergency stop — pause all autonomous tasks | PlatformAdmin | Platform-wide |
| `POST` | `/resume` | Resume from emergency stop | PlatformAdmin | Requires confirmation body |
| `GET` | `/status` | Current emergency stop status | PlatformAdmin | |
| `GET` | `/config` | Sanitized platform configuration | PlatformAdmin | |

---

**Total: ~140 endpoints** (29 groups). The prototype had ~80; growth reflects first-class tasks, learnings lifecycle, proper project scoping, document/search capabilities, and proper separation of browse vs admin catalog.

---

## 2. Key Request/Response DTOs

### CreateProjectRequest / ProjectResponse

```csharp
public sealed record CreateProjectRequest
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("template_id")] public Guid? TemplateId { get; init; }
    [JsonPropertyName("budget_ceiling")] public decimal? BudgetCeiling { get; init; }
}

public sealed record ProjectResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; } // active, paused, archived
    [JsonPropertyName("budget_ceiling")] public decimal? BudgetCeiling { get; init; }
    [JsonPropertyName("total_cost")] public required decimal TotalCost { get; init; }
    [JsonPropertyName("task_counts")] public required TaskCountsDto TaskCounts { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TaskCountsDto
{
    [JsonPropertyName("proposed")] public int Proposed { get; init; }
    [JsonPropertyName("approved")] public int Approved { get; init; }
    [JsonPropertyName("queued")] public int Queued { get; init; }
    [JsonPropertyName("running")] public int Running { get; init; }
    [JsonPropertyName("completed")] public int Completed { get; init; }
    [JsonPropertyName("failed")] public int Failed { get; init; }
    [JsonPropertyName("skipped")] public int Skipped { get; init; }
    [JsonPropertyName("cancelled")] public int Cancelled { get; init; }
}
```

### CreateTaskRequest / TaskResponse

```csharp
public sealed record CreateTaskRequest
{
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; } // agent_task, human_decision, ai_decision, action, sub_workflow
    [JsonPropertyName("agent_id")] public Guid? AgentId { get; init; }
    [JsonPropertyName("depends_on")] public List<Guid>? DependsOn { get; init; }
    [JsonPropertyName("inputs")] public JsonElement? Inputs { get; init; }
    [JsonPropertyName("budget_ceiling")] public decimal? BudgetCeiling { get; init; }
}

public sealed record TaskResponse
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("project_id")] public required Guid ProjectId { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; } // human, planner, workflow
    [JsonPropertyName("parent_task_id")] public Guid? ParentTaskId { get; init; }
    [JsonPropertyName("agent_id")] public Guid? AgentId { get; init; }
    [JsonPropertyName("agent_name")] public string? AgentName { get; init; }
    [JsonPropertyName("depends_on")] public required List<Guid> DependsOn { get; init; }
    [JsonPropertyName("inputs")] public JsonElement? Inputs { get; init; }
    [JsonPropertyName("outputs")] public JsonElement? Outputs { get; init; }
    [JsonPropertyName("cost")] public required TaskCostDto Cost { get; init; }
    [JsonPropertyName("attempts")] public required int Attempts { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("error_details")] public JsonElement? ErrorDetails { get; init; }
    [JsonPropertyName("triggered_by")] public Guid? TriggeredBy { get; init; }
    [JsonPropertyName("triggered_by_name")] public string? TriggeredByName { get; init; }
    [JsonPropertyName("approved_by")] public Guid? ApprovedBy { get; init; }
    [JsonPropertyName("approved_by_name")] public string? ApprovedByName { get; init; }
    [JsonPropertyName("created_at")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record TaskCostDto
{
    [JsonPropertyName("total_usd")] public decimal TotalUsd { get; init; }
    [JsonPropertyName("input_tokens")] public long InputTokens { get; init; }
    [JsonPropertyName("output_tokens")] public long OutputTokens { get; init; }
    [JsonPropertyName("cache_read_tokens")] public long CacheReadTokens { get; init; }
}
```

### ApproveTaskRequest / TaskTransitionResponse

```csharp
public sealed record ApproveTaskRequest
{
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record TaskTransitionResponse
{
    [JsonPropertyName("task_id")] public required Guid TaskId { get; init; }
    [JsonPropertyName("previous_status")] public required string PreviousStatus { get; init; }
    [JsonPropertyName("new_status")] public required string NewStatus { get; init; }
    [JsonPropertyName("transitive_tasks")] public required List<Guid> TransitiveTasks { get; init; }
}
```

### BulkApproveRequest / BulkApproveResponse

```csharp
public sealed record BulkApproveRequest
{
    [JsonPropertyName("task_ids")] public List<Guid>? TaskIds { get; init; } // null = all proposed
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record BulkApproveResponse
{
    [JsonPropertyName("approved")] public required List<Guid> Approved { get; init; }
    [JsonPropertyName("skipped")] public required List<SkippedTaskDto> Skipped { get; init; }
}

public sealed record SkippedTaskDto
{
    [JsonPropertyName("task_id")] public required Guid TaskId { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; } // SEGREGATION_VIOLATION, INVALID_TRANSITION, etc.
}
```

### DispatchResponse

```csharp
public sealed record DispatchResponse
{
    [JsonPropertyName("run_id")] public required Guid RunId { get; init; }
    [JsonPropertyName("dispatched_count")] public required int DispatchedCount { get; init; }
    [JsonPropertyName("task_ids")] public required List<Guid> TaskIds { get; init; }
    [JsonPropertyName("skipped")] public required List<SkippedTaskDto> Skipped { get; init; } // tasks with unmet deps
}
```

### RunAdHocRequest

```csharp
public sealed record RunAdHocRequest
{
    [JsonPropertyName("objective")] public required string Objective { get; init; }
    [JsonPropertyName("agent_id")] public Guid? AgentId { get; init; }
    [JsonPropertyName("budget_ceiling")] public decimal? BudgetCeiling { get; init; }
}

public sealed record RunAdHocResponse
{
    [JsonPropertyName("task_id")] public required Guid TaskId { get; init; }
    [JsonPropertyName("execution_id")] public required Guid ExecutionId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
}
```

### WorkflowRunRequest / WorkflowRunResponse

```csharp
public sealed record WorkflowRunRequest
{
    [JsonPropertyName("context")] public JsonElement? Context { get; init; }
}

public sealed record WorkflowRunResponse
{
    [JsonPropertyName("run_id")] public required Guid RunId { get; init; }
    [JsonPropertyName("workflow_id")] public required Guid WorkflowId { get; init; }
    [JsonPropertyName("workflow_name")] public required string WorkflowName { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("tasks")] public required List<TaskSummaryDto> Tasks { get; init; }
    [JsonPropertyName("started_at")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; init; }
}
```

### TaskSummaryDto (lightweight, for embedding in workflow runs)

```csharp
public sealed record TaskSummaryDto
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("parent_task_id")] public Guid? ParentTaskId { get; init; }
    [JsonPropertyName("agent_name")] public string? AgentName { get; init; }
    [JsonPropertyName("cost")] public required TaskCostDto Cost { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}
```

### SendMessageRequest (Chat)

```csharp
public sealed record SendMessageRequest
{
    [JsonPropertyName("content")] public required string Content { get; init; }
    [JsonPropertyName("agent_id")] public Guid? AgentId { get; init; }
}
// Response: text/event-stream (SSE)
```

### HumanInputResponse

```csharp
public sealed record HumanInputResponseRequest
{
    [JsonPropertyName("action")] public required string Action { get; init; } // approve, reject, escalate
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("data")] public JsonElement? Data { get; init; }
}
```

### Standard Envelope

```csharp
public sealed record ApiResponse<T>
{
    [JsonPropertyName("success")] public required bool Success { get; init; }
    [JsonPropertyName("data")] public T? Data { get; init; }
    [JsonPropertyName("error")] public ApiError? Error { get; init; }
    [JsonPropertyName("metadata")] public required ApiMetadata Metadata { get; init; }
}

public sealed record ApiError
{
    [JsonPropertyName("code")] public required string Code { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("details")] public JsonElement? Details { get; init; }
}

public sealed record ApiMetadata
{
    [JsonPropertyName("request_id")] public required string RequestId { get; init; }
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
}

public sealed record PaginatedData<T>
{
    [JsonPropertyName("items")] public required List<T> Items { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("limit")] public required int Limit { get; init; }
    [JsonPropertyName("offset")] public required int Offset { get; init; }
}

public sealed record CursorPaginatedData<T>
{
    [JsonPropertyName("items")] public required List<T> Items { get; init; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
    [JsonPropertyName("has_more")] public required bool HasMore { get; init; }
}
```

---

## 3. SSE Event Types

| Event Type | Payload Shape | Source Stream |
|------------|--------------|--------------|
| `connected` | `{ session_id }` | All |
| `task.proposed` | `{ task_id, title, type }` | Project, Task |
| `task.approved` | `{ task_id, approved_by }` | Project, Task |
| `task.started` | `{ task_id, agent_id, attempt }` | Project, Task |
| `task.progress` | `{ task_id, message, percent? }` | Project, Task |
| `task.completed` | `{ task_id, output_summary, cost }` | Project, Task |
| `task.failed` | `{ task_id, error, attempt }` | Project, Task |
| `task.cancelled` | `{ task_id, cancelled_by }` | Project, Task |
| `agent.response.chunk` | `{ session_id, content, role }` | Chat |
| `agent.response.done` | `{ session_id, usage }` | Chat |
| `agent.tool.start` | `{ session_id, tool_name, args }` | Chat |
| `agent.tool.result` | `{ session_id, tool_name, result_summary, result_length }` | Chat |
| `workflow.started` | `{ run_id, workflow_name }` | Project |
| `workflow.completed` | `{ run_id, status, cost }` | Project |
| `human_input.requested` | `{ request_id, task_id, prompt }` | Project, Notifications |
| `human_input.resolved` | `{ request_id, action, resolved_by }` | Project |
| `learning.created` | `{ learning_id, kind, title }` | Project |
| `learning.retracted` | `{ learning_id, reason }` | Project |
| `cost.threshold` | `{ project_id, percent, ceiling }` | Notifications |
| `project.paused` | `{ project_id, reason }` | Notifications |
| `done` | `{}` | Chat (sentinel) |

---

## 4. Error Codes

| Code | HTTP Status | Meaning |
|------|-------------|---------|
| `NOT_FOUND` | 404 | Resource not found |
| `VALIDATION_ERROR` | 422 | Request body validation failed |
| `UNAUTHORIZED` | 401 | Missing or invalid credentials |
| `FORBIDDEN` | 403 | Insufficient role |
| `BUDGET_EXCEEDED` | 409 | Task or project budget ceiling exceeded |
| `SEGREGATION_VIOLATION` | 409 | triggered_by == approved_by |
| `DEPENDENCY_NOT_MET` | 409 | Task depends on incomplete tasks |
| `INVALID_TRANSITION` | 409 | Invalid status transition (e.g., approve a running task) |
| `CONCURRENT_EXECUTION` | 409 | Active tasks already running for dispatch |
| `IDEMPOTENCY_CONFLICT` | 409 | Idempotency key reused with different body |
| `AGENT_DISABLED` | 422 | Agent is disabled in catalog |
| `GRAPH_INVALID` | 422 | Workflow graph has cycles or missing nodes |
| `RATE_LIMITED` | 429 | API rate limit exceeded (per-user or per-API-key) |
| `QUOTA_EXCEEDED` | 429 | Model quota/rate limit reached |
| `EMERGENCY_STOP` | 503 | Platform in emergency stop mode |
| `UPSTREAM_ERROR` | 502 | LLM provider or external service failure |

---

## 5. Pagination & Filtering Conventions

### Standard Query Parameters (all list endpoints)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit` | int | 20 | Page size (max 100) |
| `offset` | int | 0 | Skip N items (use for low-volume resources) |
| `sort_by` | string | `created_at` | Sort field (validated against per-resource allowlist) |
| `sort_dir` | string | `desc` | `asc` or `desc` |

### Cursor-Based Pagination (high-volume endpoints)

Events, cost timeline, audit trails, and workflow runs support cursor-based pagination as an alternative to offset. When `cursor` is provided, `offset` is ignored.

| Parameter | Type | Description |
|-----------|------|-------------|
| `cursor` | string | Opaque token from previous response's `next_cursor` |
| `limit` | int | Page size (max 100) |

Cursor-paginated responses include:
```json
{
  "items": [...],
  "next_cursor": "eyJ0IjoiMjAyNi0wNS0xMVQxMDowMDowMFoiLCJpIjoiYWJjMTIzIn0=",
  "has_more": true
}
```

Cursor-enabled endpoints: events, cost timeline, workflow runs, learnings, audit trails, session messages.

### Allowed `sort_by` Fields Per Resource

Unknown `sort_by` values return `VALIDATION_ERROR`. This prevents schema leakage and unindexed sorts.

| Resource | Allowed `sort_by` Values |
|----------|-------------------------|
| Projects | `created_at`, `updated_at`, `name`, `status`, `total_cost` |
| Tasks | `created_at`, `started_at`, `completed_at`, `status`, `type`, `cost` |
| Workflows | `created_at`, `updated_at`, `name` |
| Workflow Runs | `started_at`, `completed_at`, `status` |
| Sessions | `created_at`, `updated_at`, `status` |
| Learnings | `created_at`, `confidence`, `kind`, `status` |
| Artifacts | `created_at`, `type`, `format` |
| Documents | `created_at`, `extraction_status` |
| Events | `timestamp`, `event_type` |

### Common Filters (query params)

| Filter | Applies To | Example |
|--------|-----------|---------|
| `status` | Tasks, workflows, sessions | `?status=running&status=failed` (multi-value) |
| `type` | Tasks | `?type=agent_task` |
| `agent_id` | Tasks, artifacts, learnings | `?agent_id={uuid}` |
| `source` | Tasks | `?source=human` |
| `kind` | Learnings | `?kind=insight` |
| `tags` | Learnings | `?tags=security,compliance` (comma-separated) |
| `from` / `to` | Events, costs | `?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z` |
| `q` | Documents, events | `?q=search+term` (text search) |

### Search Request Body

```json
{
  "query": "string",
  "mode": "semantic | structured | hybrid",
  "scope": ["artifacts", "learnings", "events", "documents"],
  "filters": { "status": "active", "agent_id": "..." },
  "limit": 20,
  "offset": 0
}
```

---

## 6. Versioning Strategy

| Aspect | Approach |
|--------|----------|
| **URL prefix** | `/api/v1/...` — version in URL path |
| **Breaking changes** | New major version (`/api/v2/...`) with parallel support |
| **Non-breaking additions** | New fields, new endpoints — no version bump |
| **Deprecation** | `Sunset` header + 90-day deprecation window |
| **Version header** | `Api-Version` response header on every response |
| **Client negotiation** | Not supported — clients target a specific version |
| **Internal API** | No separate versioning — internal services use service mesh |

### Compatibility Rules
- New optional fields on responses: **non-breaking**
- New optional fields on requests: **non-breaking**
- Removing a field: **breaking** → new version
- Changing field type: **breaking** → new version
- New endpoints: **non-breaking**
- Removing endpoints: **breaking** → deprecate first, then remove in next version
