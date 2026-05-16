# R16: API Design — REST Endpoints, DTOs, and Contracts

## Prompt for claude.ai

---

You are a senior API architect designing the complete REST API surface for the **Agentic Workforce Platform** — an AI agent orchestration system for a regulated bank, built in C# / ASP.NET Core 9, deployed on Azure Container Apps.

This is a greenfield design. The prototype had ~80 endpoints. Design from first principles using the architectural decisions below.

### The Unifying Primitive

**Task is the primitive. Project is the scope. Session is transport.**

- **Project** scopes everything — budget, context, team, lifecycle. Every resource is nested under `/api/v1/projects/{projectId}/...`
- **Task** is the primitive — the one thing the system manipulates. Tasks have types (agent_task, human_decision, ai_decision, action, sub_workflow), statuses, dependencies, inputs, outputs, cost.
- **Session** is transport — conversations between humans and agents. Linked to projects and optionally tasks.

### Authentication & Authorization

Two auth schemes accepted on every endpoint via `AddPolicyScheme`:
- **Entra ID JWT** (Bearer token) — for SPA and CLI users
- **API Key** (`X-Api-Key` header) — for programmatic consumers

**Platform roles** (Entra ID): `platform_admin`, `member`
**Project roles** (per-project DB): `owner`, `operator`, `reviewer`, `viewer`

| Permission | Owner | Operator | Reviewer | Viewer | Platform Admin |
|-----------|-------|----------|----------|--------|----------------|
| Read project data | Yes | Yes | Yes | Yes | Override |
| Run tasks / executions | Yes | Yes | No | No | Override |
| Approve at gates | Yes | Yes | Yes | No | Override |
| Manage project (team, budget, settings) | Yes | No | No | No | Override |
| Add principles to PCD | Yes | Yes | No | No | Override |
| Retract learnings | Yes | Yes | No | No | Override |
| Edit learnings | Yes | No | No | No | Override |
| Delete project | Yes | No | No | No | Override |

Segregation of duties: `triggered_by != approved_by` on approval endpoints.

### Response Envelope

All responses use a standard envelope:

```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "metadata": { "request_id": "...", "timestamp": "..." }
}
```

Paginated responses:
```json
{
  "success": true,
  "data": {
    "items": [...],
    "total": 142,
    "limit": 20,
    "offset": 0
  }
}
```

Error responses:
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "BUDGET_EXCEEDED",
    "message": "Task budget ceiling of $1.00 exceeded ($1.02 spent)",
    "details": { "ceiling": 1.00, "spent": 1.02 }
  }
}
```

### Streaming

SSE endpoints return `text/event-stream` with:
- `event: connected` on initial subscription
- `event: <type>` for each event (task.started, agent.response.chunk, etc.)
- `event: done` sentinel on completion
- `: keepalive` comments every 30s

### Idempotency

Endpoints that create resources accept an `Idempotency-Key` header. Responses are cached in Redis for safe retries.

### Resource Groups to Design

Design endpoints for ALL of the following groups. For each endpoint specify: method, path, purpose, auth level, request body (if any), response shape, notes.

**1. Projects**
- CRUD for projects
- Project status transitions (active, paused, archived)
- Project overview/summary

**2. Project Team (agents)**
- List agents on project
- Add/remove agent from catalog
- Update agent user prompt (versioned)
- Update agent constraints
- Seed team from template

**3. Project Members (humans)**
- List members
- Add/remove member with role
- Update member role
- Transfer ownership

**4. Tasks (the primitive — this is the most important group)**
- List tasks with filters (status, type, source, agent)
- Get task detail (with attempts, artifacts, learnings, events)
- Create task manually (human adds to planner board)
- Approve/reject proposed tasks (move from proposed → approved or cancelled)
- Retry failed tasks
- Cancel running tasks
- Get task cost breakdown
- Get task dependency graph
- Bulk approve (approve all proposed tasks)

**5. Task Execution**
- Start execution of approved tasks (dispatch)
- Run ad-hoc objective (creates + executes a task in one call)
- Get execution status (real-time via SignalR, polling fallback)

**6. Workflows**
- CRUD for workflow definitions
- Import workflow from YAML
- Validate workflow graph (dry run)
- Save workflow graph canvas state (for visual editor)
- Run a workflow (creates tasks from nodes, dispatches)
- List workflow runs with status
- Get workflow run detail

**7. Workflow Schedules**
- CRUD for cron-based schedules
- Enable/disable schedule
- List upcoming scheduled runs

**8. Human Input / Approval Gates**
- List pending human input requests for a project
- Respond to a human input request (approve/reject/escalate with reason)
- Get approval audit trail for a task

**9. Project Context (PCD)**
- Get full PCD content with version/size metrics
- Get PCD change history
- Add principle to PCD (human direction)
- Add guardrail to PCD (human direction)
- Remove principle/guardrail (owner only)

**10. Knowledge & Learnings**
- List learnings with filters (kind, status, confidence, agent, tags)
- Get learning detail with evidence
- Retract a learning (mark as wrong)
- Edit a learning
- Supersede a learning (create corrected version)
- Propose promotion to platform
- Search learnings semantically (vector search)
- Find similar learnings (dedup check)

**11. Decisions**
- List active decisions
- Get decision detail
- Create decision manually

**12. Intent**
- Get current intent
- Get intent revision history
- Create new intent revision

**13. Artifacts**
- List artifacts with filters (type, format, agent)
- Get artifact detail
- Get artifact content (inline render for markdown, download URL for binary)
- Delete artifact (owner only)

**14. Documents (uploads)**
- Upload document (multipart/form-data)
- List documents with extraction status
- Get document detail (metadata + extraction status)
- Get document extracted text
- Delete document
- Search documents semantically

**15. Sessions & Chat**
- Create session (for a project, optionally linked to a task)
- List sessions
- Get session detail (with messages)
- Send message to agent (streaming SSE response)
- Suspend/resume/complete session
- Get session messages (paginated)

**16. Console / Events**
- Get project event history (paginated, filterable)
- SSE stream: project events (real-time console)
- SSE stream: task events (filtered to one task)
- SSE stream: user notifications

**17. Project Costs**
- Get project cost summary (total, by agent, by model)
- Get cost timeline (hourly aggregation for charts)
- Get token economics (input/output ratio, cache hit rate)

**18. Project Search**
- Search across artifacts, learnings, events, documents within a project
- Semantic search mode (pgvector)
- Structured search mode (filters)

**19. Admin: Platform Dashboard**
- Platform health check (DB, Redis, Foundry)
- Platform overview metrics (active projects, running tasks, total cost)
- Platform cost breakdown (by model, by project, by agent)
- Cost timeline (hourly, for charts)
- Model quota status

**20. Admin: Agent Catalog**
- CRUD for agent catalog entries
- Update agent system prompt (versioned)
- Get prompt version history
- Enable/disable agent
- Seed catalog from YAML
- Agent usage stats (projects using it, cost, success rate)

**21. Admin: Platform Knowledge**
- List platform-promoted learnings
- List pending promotions
- Approve/reject promotion
- Edit/retract platform learnings
- Demote (remove from platform)

**22. Admin: Users**
- CRUD for users
- Reset password
- Create/list/revoke API keys for any user
- List configured roles

**23. Admin: Templates**
- CRUD for project templates
- Get template detail (composition rules, guardrails)

**24. Admin: Emergency**
- Emergency stop (pause all autonomous tasks platform-wide)
- Platform configuration (sanitized view)

**25. Auth**
- SSE token exchange (JWT → short-lived Redis token)
- Get current user profile
- Update own profile
- Change own password
- Create/list/revoke own API keys

**26. Health**
- Liveness (`/alive`)
- Readiness (`/health/ready`)
- Detailed health (`/health/detailed`)

### Output Format

**1. Endpoint Reference Table**
For each group, a table with columns: Method | Path | Purpose | Auth | Notes

Use consistent path patterns:
- Project-scoped: `/api/v1/projects/{projectId}/...`
- Platform admin: `/api/v1/admin/...`
- Auth: `/api/v1/auth/...`
- Health: `/health/...` (no `/api/v1` prefix)

**2. Key Request/Response DTOs**
For the most important endpoints (task CRUD, project CRUD, workflow run, chat, approval), show the C# DTO classes with JSON property names.

**3. SSE Event Types**
Complete list of SSE event types with payload shapes.

**4. Error Codes**
Standard error codes (BUDGET_EXCEEDED, SEGREGATION_VIOLATION, DEPENDENCY_NOT_MET, etc.) with HTTP status mappings.

**5. Pagination & Filtering Conventions**
Standard query parameters for list endpoints.

**6. Versioning Strategy**
How the API is versioned and how breaking changes are handled.

### Constraints

- REST over HTTP/JSON (no GraphQL, no gRPC for client-facing API)
- ASP.NET Core Minimal API style (not Controllers, unless the group is large enough to warrant it)
- All list endpoints support pagination (`limit`, `offset`) and sorting (`sort_by`, `sort_dir`)
- All timestamps in ISO 8601 UTC
- All IDs are UUIDs
- Consistent naming: snake_case for JSON properties, PascalCase for C# properties (`[JsonPropertyName]`)
- No nested resource creation (create the parent first, then add children)
- Streaming endpoints use SSE, not WebSocket (CLI compatibility)

Keep total response under 5000 words. Tables preferred over prose. Show DTO code for the top 10 most important endpoints.

---

## After Research

Save claude.ai's response as: `docs/098-research/R16-response-api-design.md`
