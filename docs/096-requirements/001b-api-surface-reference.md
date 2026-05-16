# API Surface Reference (SRS Appendix B)

**Companion to:** `029-mission-control-requirements.md` Section 4.23
**Generated from:** actual router definitions, CLI entry points, and dependency annotations

---

## REST API Endpoints

Base URL: `/api/v1` (mounted via `modules/backend/main.py`)

All responses use the `ApiResponse` envelope: `{ success, data, error, metadata }`.
Paginated responses use: `{ items, total, limit, offset }`.

### Health (root — no `/api/v1` prefix)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/health` | Liveness check (process running) | Public | `dict` (`{"status": "healthy"}`) |
| `GET` | `/health/ready` | Readiness check (DB + Redis in parallel) | Public | `dict` or `503` with unhealthy details |
| `GET` | `/health/detailed` | Component-by-component status with latency | Public | `dict` (app info + per-component checks) |

### Authentication (`/api/v1/auth`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `POST` | `/login` | Email/password login | Public | `ApiResponse(LoginResponse)` |
| `POST` | `/refresh` | Exchange refresh token for new token pair | Public | `ApiResponse(TokenRefreshResponse)` |
| `POST` | `/sse-token` | Exchange JWT for short-lived SSE token (Redis, 30s TTL) | CurrentUser | `ApiResponse(SseTokenResponse)` |
| `GET` | `/me` | Get current authenticated user | CurrentUser | `ApiResponse(UserResponse)` |
| `PATCH` | `/me` | Update own profile (display name) | CurrentUser | `ApiResponse(UserResponse)` |
| `PUT` | `/me/current-mission` | Set or clear current active mission | CurrentUser | `ApiResponse(UserResponse)` |
| `POST` | `/api-keys` | Create API key for self | CurrentUser | `ApiResponse` (includes full key once) |
| `GET` | `/api-keys` | List own API keys | CurrentUser | `ApiResponse` (list of `ApiKeyResponse`) |
| `DELETE` | `/api-keys/{key_id}` | Revoke own API key | CurrentUser | `ApiResponse` |
| `POST` | `/change-password` | Change own password | CurrentUser | `ApiResponse` |

### Agents (`/api/v1/agents`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `POST` | `/chat` | Chat with agent (auto-creates session) | Public | `ApiResponse(ChatResponse)` | |
| `POST` | `/chat/stream` | Chat with agent via SSE stream | Public | `StreamingResponse` (SSE) | Idempotency dep |
| `GET` | `/registry` | List all available agents with capabilities | Public | `ApiResponse(list[AgentInfo])` | |

### Catalog (`/api/v1/catalog/agents`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/` | List catalog agents (filterable by type) | Public | `ApiResponse` |
| `GET` | `/{agent_name}` | Get agent detail including system prompt | Public | `ApiResponse(AgentCatalogDetailResponse)` |

### Sessions (`/api/v1/sessions`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `POST` | `/` | Create session | Public | `ApiResponse(SessionResponse)` 201 | Idempotency |
| `GET` | `/{session_id}` | Get session by ID | Public | `ApiResponse(SessionResponse)` | |
| `PATCH` | `/{session_id}` | Update session fields | Public | `ApiResponse(SessionResponse)` | |
| `GET` | `/` | List sessions (paginated) | Public | Paginated `dict` | Filters: user_id, mission_id, status |
| `POST` | `/{session_id}/suspend` | Suspend session | Public | `ApiResponse(SessionResponse)` | Requires `reason` query param |
| `POST` | `/{session_id}/resume` | Resume suspended session | Public | `ApiResponse(SessionResponse)` | |
| `POST` | `/{session_id}/complete` | Mark session complete | Public | `ApiResponse(SessionResponse)` | |
| `POST` | `/{session_id}/channels` | Bind communication channel | Public | `ApiResponse(ChannelResponse)` 201 | |
| `DELETE` | `/{session_id}/channels/{type}/{id}` | Unbind channel | Public | `204 No Content` | |
| `GET` | `/by-channel/{type}/{id}` | Find active session by channel | Public | `ApiResponse(SessionResponse)` | |
| `GET` | `/{session_id}/messages` | List messages (paginated) | Public | Paginated `dict` | |
| `POST` | `/{session_id}/messages` | Send message, stream agent SSE | Public | `StreamingResponse` (SSE) | Requires `mission_id` query param |

### Executions (`/api/v1/executions`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `GET` | `/` | List execution records | Public | `ApiResponse(ExecutionListResponse)` | Filters: status, roster_name, objective_category, mission_id |
| `GET` | `/{id}` | Execution detail | Public | `ApiResponse(ExecutionRecordDetailResponse)` | |
| `GET` | `/{id}/decisions` | Decision audit trail | Public | `ApiResponse` | |
| `GET` | `/{id}/cost` | Cost breakdown | Public | `ApiResponse(ExecutionCostBreakdown)` | |
| `POST` | `/{id}/execute` | Start execution (Temporal or background) | Public | `ApiResponse` | Idempotency |
| `POST` | `/{id}/cancel` | Cancel execution | Public | `ApiResponse` | |
| `POST` | `/{id}/approve` | Submit Temporal approval signal | Public | `ApiResponse` | Requires Temporal enabled |
| `GET` | `/{id}/status` | Status query (Temporal or DB fallback) | Public | `ApiResponse` | |

### Missions (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `POST` | `/` | Create mission | CurrentUser | `ApiResponse(MissionResponse)` | Idempotency; platform tier requires admin |
| `GET` | `/` | List missions | CurrentUser | `ApiResponse` | Admins see all; users see owned/member |
| `GET` | `/{id}` | Mission detail | CurrentUser | `ApiResponse(MissionResponse)` | Owner, member, or admin |
| `DELETE` | `/{id}` | Delete mission (cascading) | CurrentUser | `ApiResponse` | Owner or admin |
| `PATCH` | `/{id}` | Update mission fields | CurrentUser | `ApiResponse(MissionResponse)` | Owner or admin |
| `GET` | `/{id}/environment` | Execution environment status | Public | `ApiResponse` | |
| `GET` | `/{id}/narrative` | Narrative timeline events | CurrentUser | `ApiResponse` | From mission_events table |

### Missions — Team (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/{id}/team` | List agent team members | Public | `ApiResponse` |
| `POST` | `/{id}/team` | Add agent from catalog to team | Public | `ApiResponse` |
| `POST` | `/{id}/team/seed` | Seed team from all enabled catalog agents | Public | `ApiResponse` |
| `DELETE` | `/{id}/team/{member_id}` | Remove agent from team | Public | `ApiResponse` |
| `PUT` | `/{id}/team/{member_id}/prompt` | Update team member user prompt (versioned) | Public | `ApiResponse` |
| `PATCH` | `/{id}/team/{member_id}/constraints` | Merge custom constraints JSON | Public | `ApiResponse` |
| `GET` | `/{id}/team/{member_id}/prompt-history` | User prompt version history | Public | `ApiResponse` |
| `GET` | `/{id}/members` | List human members | Public | `ApiResponse` |
| `POST` | `/{id}/members` | Add human member with role | Public | `ApiResponse` |
| `PATCH` | `/{id}/members/{user_id}` | Update member role | Public | `ApiResponse` |
| `DELETE` | `/{id}/members/{user_id}` | Remove human member | Public | `ApiResponse` |

### Missions — Plan (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `GET` | `/{id}/plan` | Get plan with task states (lazy-created) | CurrentUser | `ApiResponse(MissionPlanResponse)` | |
| `PATCH` | `/{id}/plan` | Update plan settings (gate_mode, status, budget, veto, order) | CurrentUser | `ApiResponse(MissionPlanResponse)` | EventBus |
| `POST` | `/{id}/plan/refine` | Refine plan via Planning Agent (Temporal) | CurrentUser | `ApiResponse` | EventBus, Temporal |
| `POST` | `/{id}/plan/run-objective` | Unified run: refine → approve → dispatch | CurrentUser | `ApiResponse` | EventBus, Temporal; 409 if active tasks |
| `POST` | `/{id}/plan/dispatch` | Dispatch all approved tasks as Temporal workflow | CurrentUser | `ApiResponse(DispatchApprovedResponse)` | EventBus |
| `POST` | `/{id}/plan/tasks` | Add manually-created proposed task | CurrentUser | `ApiResponse(MissionPlanResponse)` | EventBus |
| `POST` | `/{id}/plan/tasks/{task_id}/approve` | Approve task + transitive dependencies | CurrentUser | `ApiResponse(TaskTransitionResponse)` | EventBus |
| `POST` | `/{id}/plan/tasks/{task_id}/reject` | Reject task + cancel transitive dependents | CurrentUser | `ApiResponse(TaskTransitionResponse)` | EventBus |
| `POST` | `/{id}/plan/tasks/{task_id}/retry` | Retry failed task (move to approved) | CurrentUser | `ApiResponse(PlanTaskStateResponse)` | EventBus |

### Missions — Context & Console (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/{id}/context` | Full MCD content with version/size metrics | Public | `ApiResponse` |
| `GET` | `/{id}/context/history` | MCD change history | Public | `ApiResponse` |
| `GET` | `/{id}/console/history` | Unified console timeline (messages + execution events) | Public | `ApiResponse` |

### Missions — Artifacts (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/{id}/artifacts` | List persisted artifacts (newest first) | CurrentUser | `ApiResponse` |

### Missions — Costs (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/{id}/costs` | Token economics scoped to mission | CurrentUser | `ApiResponse` |

### Missions — Workflows & Schedules (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `GET` | `/{id}/workflows` | List DB-stored workflow definitions | Public | `ApiResponse` | |
| `GET` | `/{id}/workflow-runs` | List workflow runs (paginated) | Public | `ApiResponse` | Filters: workflow_name, status |
| `POST` | `/{id}/workflows` | Create blank workflow definition | Public | `ApiResponse` | |
| `POST` | `/{id}/workflows/import` | Import workflow from YAML template | Public | `ApiResponse` | |
| `GET` | `/{id}/workflows/{name}` | Workflow detail with steps | Public | `ApiResponse` | |
| `POST` | `/{id}/workflows/{name}/run` | Run workflow by name | Public | `ApiResponse` | |
| `DELETE` | `/{id}/workflows/{wf_id}` | Delete workflow definition | CurrentUser | `ApiResponse` | Admin role check |
| `PUT` | `/{id}/workflows/{wf_id}/graph` | Save workflow graph canvas state | Public | `ApiResponse` | |
| `POST` | `/{id}/workflows/{wf_id}/validate` | Validate workflow graph (dry run) | Public | `ApiResponse` | |
| `GET` | `/{id}/workflow-runs/{run_id}` | Single workflow run detail | Public | `ApiResponse` | |
| `GET` | `/{id}/schedules` | List workflow schedules | Public | `ApiResponse` | |
| `POST` | `/{id}/schedules` | Create cron-based schedule | Public | `ApiResponse` | |
| `PATCH` | `/{id}/schedules/{sched_id}` | Update schedule configuration | Public | `ApiResponse` | |
| `DELETE` | `/{id}/schedules/{sched_id}` | Delete schedule | Public | `ApiResponse` | |
| `POST` | `/{id}/architect/chat` | Chat with Workflow Architect | Public | `ApiResponse` | May include graph_proposal |
| `GET` | `/{id}/human-input/pending` | List pending human input requests | Public | `ApiResponse` | |
| `POST` | `/{id}/human-input/{req_id}/respond` | Respond to human input request | Public | `ApiResponse` | Sends Temporal signal |

### Missions — Knowledge (`/api/v1/missions`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/{id}/knowledge` | Unified knowledge view | CurrentUser | `ApiResponse` |
| `GET` | `/{id}/learnings` | List learnings (paginated) | CurrentUser | `ApiResponse` |
| `GET` | `/{id}/learnings/{lid}` | Learning detail with evidence | CurrentUser | `ApiResponse(MissionLearningResponse)` |
| `GET` | `/{id}/learnings/{lid}/evidence` | Paginated evidence for a learning | CurrentUser | `ApiResponse` |
| `GET` | `/{id}/learnings/{lid}/similar` | Vector-similar entries | CurrentUser | `ApiResponse` |
| `GET` | `/{id}/intent` | Current intent revision | CurrentUser | `ApiResponse` |
| `GET` | `/{id}/intent/history` | Intent revision timeline (paginated) | CurrentUser | `ApiResponse` |
| `POST` | `/{id}/intent` | Create new intent revision | CurrentUser | `ApiResponse(MissionIntentResponse)` |

### Platform Knowledge (`/api/v1/knowledge`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/search` | Unified search (structured / hybrid / semantic) | AdminRole | `ApiResponse` |
| `GET` | `/similar/{table}/{id}` | Find vector-similar entries platform-wide | AdminRole | `ApiResponse` |
| `GET` | `/learnings` | Platform-promoted learnings across missions | AdminRole | `ApiResponse` |
| `GET` | `/decisions` | Cross-mission archived decisions | AdminRole | `ApiResponse` |
| `GET` | `/milestones` | Cross-mission milestone summaries | AdminRole | `ApiResponse` |
| `GET` | `/tags` | Domain tag taxonomy | AdminRole | `ApiResponse` |

### Workflows — Legacy (`/api/v1/workflows`)

| Method | Path | Purpose | Auth | Response | Notes |
|--------|------|---------|------|----------|-------|
| `GET` | `/` | List workflow template definitions | Public | `ApiResponse` | |
| `GET` | `/{name}` | Workflow template detail | Public | `ApiResponse` | |
| `GET` | `/missions` | List execution records | Public | `ApiResponse` | Reads from execution_records |
| `GET` | `/missions/{id}` | Execution record detail | Public | `ApiResponse` | |
| `POST` | `/missions/{id}/cancel` | Cancel execution | Public | `ApiResponse` | |

### Workshop (`/api/v1/workshop`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `POST` | `/{agent_name}/run` | Run agent in sandbox with optional overrides | Public | `ApiResponse` |
| `GET` | `/{agent_name}/cached-inputs` | Cached outputs from previous runs | Public | `ApiResponse` |
| `GET` | `/runs` | List workshop execution records | Public | `ApiResponse` |

### Streams (`/api/v1/streams`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `GET` | `/runs/{session_id}` | Run events SSE (filtered by session) | SseUser | `StreamingResponse` (SSE) |
| `GET` | `/notifications` | User notifications SSE | SseUser | `StreamingResponse` (SSE) |
| `GET` | `/missions/{mission_id}` | Mission console events SSE | SseUser | `StreamingResponse` (SSE) |

### Client Errors (`/api/v1/client-errors`)

| Method | Path | Purpose | Auth | Response |
|--------|------|---------|------|----------|
| `POST` | `/` | Report frontend error (logged server-side) | Public | `ApiResponse(ClientErrorReceived)` |

### Admin (`/api/v1/admin`)

All endpoints require `AdminRole`.

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| `GET` | `/catalog` | List all catalog entries (incl. disabled) | `ApiResponse` |
| `POST` | `/catalog` | Create agent catalog entry | `ApiResponse` |
| `PATCH` | `/catalog/{name}` | Update catalog entry fields | `ApiResponse` |
| `DELETE` | `/catalog/{name}` | Delete catalog entry | `ApiResponse` |
| `PUT` | `/catalog/{name}/prompt` | Update agent system prompt (versioned) | `ApiResponse` |
| `GET` | `/catalog/{name}/prompt-history` | System prompt version history | `ApiResponse` |
| `POST` | `/seed-catalog` | Seed catalog from YAML configs | `ApiResponse` |
| `GET` | `/metrics/overview` | Platform overview (counts + total cost) | `ApiResponse` |
| `GET` | `/metrics/costs` | Cost breakdown by agent and status | `ApiResponse` |
| `GET` | `/metrics/agents` | Per-agent utilisation (runs, cost, duration) | `ApiResponse` |
| `GET` | `/metrics/activity` | Recent platform activity | `ApiResponse` |
| `GET` | `/metrics/token-economics` | Per-agent token breakdown | `ApiResponse` |
| `GET` | `/metrics/cache-performance` | Cache hit rates and savings | `ApiResponse` |
| `GET` | `/metrics/cost-timeline` | Hourly cost aggregation for charts | `ApiResponse` |
| `GET` | `/metrics/expensive-calls` | Top N most expensive LLM calls | `ApiResponse` |
| `GET` | `/metrics/model-distribution` | Call count and cost per model | `ApiResponse` |
| `GET` | `/metrics/output-efficiency` | Output/input ratio by agent | `ApiResponse` |
| `GET` | `/metrics/cost-alerts` | Active cost alert rules | `ApiResponse` |
| `GET` | `/config` | Sanitized platform configuration | `ApiResponse` |
| `POST` | `/system/health` | Comprehensive health check (all services) | `ApiResponse` |
| `GET` | `/daemon/status` | Mission plan daemon heartbeat | `ApiResponse` |
| `POST` | `/dispatch/emergency-stop` | Pause all active autonomous plans | `ApiResponse` |

### Admin Users (`/api/v1/admin/users`)

All endpoints require `AdminRole`.

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| `POST` | `/` | Create user | `ApiResponse(UserResponse)` |
| `GET` | `/` | List users (paginated) | `ApiResponse` |
| `GET` | `/roles` | List configured roles and levels | `ApiResponse` |
| `GET` | `/{user_id}` | Get user detail | `ApiResponse(UserResponse)` |
| `PATCH` | `/{user_id}` | Update user (profile, role, active) | `ApiResponse(UserResponse)` |
| `DELETE` | `/{user_id}` | Deactivate user (soft delete + revoke keys) | `ApiResponse` |
| `POST` | `/{user_id}/reset-password` | Admin password reset | `ApiResponse` |
| `POST` | `/{user_id}/api-keys` | Create API key for any user | `ApiResponse` |
| `GET` | `/{user_id}/api-keys` | List user's API keys | `ApiResponse` |
| `DELETE` | `/{user_id}/api-keys/{key_id}` | Revoke user's API key | `ApiResponse` |

---

## Auth Pattern Summary

| Pattern | Mechanism | Source |
|---------|-----------|--------|
| **CurrentUser** | JWT Bearer token or `X-API-Key` header | `core/dependencies.py` |
| **AdminRole** | CurrentUser + `has_role("admin")` check | `core/dependencies.py` |
| **SseUser** | SSE token (Redis single-use, 30s TTL) or JWT fallback (no API keys) | `core/dependencies.py` |
| **Public** | No auth dependency in code (may have network-level auth) | — |
| **Idempotency** | `Idempotency-Key` header → Redis-cached response (safe retries) | `core/dependencies.py` |
| **EventBus** | Injected `SessionEventBus` for real-time event publishing | `core/dependencies.py` |

---

## CLI Commands

### `mc.py` (User CLI)

Root options: `--verbose/-v`, `--debug/-d`, `--quiet/-q`

| Command Path | Options / Arguments | Purpose |
|---|---|---|
| `mc login` | `--email`, `--password` | Authenticate and store token |
| `mc logout` | — | Clear stored credentials |
| `mc mission create` | `--name` (required), `--description`, `--brief`, `--repo-url`, `--repo-root`, `--roster`, `--budget`, `--tier` | Create a new mission |
| `mc mission list` | `--status`, `--limit` | List missions |
| `mc mission use` | `MISSION` (argument) | Set current active mission |
| `mc mission info` | `MISSION` (optional) | Show mission detail |
| `mc mission set-gate` | `MISSION`, `--mode` (off/interactive/autonomous) | Set gate mode |
| `mc mission set-budget` | `MISSION`, `--amount` | Set budget ceiling |
| `mc mission archive` | `MISSION` | Archive a mission |
| `mc mission delete` | `MISSION`, `--confirm` | Permanently delete a mission |
| `mc mission unarchive` | `MISSION` | Restore archived mission |
| `mc run` | `OBJECTIVE` (argument), `--mission`, `--gate`, `--budget`, `--wait/--no-wait`, `--monitor/--no-monitor` | Unified plan-and-execute objective |
| `mc retry` | `--mission`, `--task-id`, `--all-failed` | Retry failed plan tasks |
| `mc status` | `--mission` | Show current mission/plan status |
| `mc history` | `--mission`, `--limit`, `--status` | List execution history |
| `mc artifacts list` | `--mission`, `--type`, `--limit` | List mission artifacts |
| `mc artifacts show` | `ARTIFACT_ID`, `--mission` | Show artifact detail |
| `mc report list` | `--mission`, `--limit` | List available reports |
| `mc report show` | `REPORT_ID`, `--mission` | Display a report |
| `mc trace` | `--mission`, `--execution`, `--workflow`, `--last`, `--filter`, `--json` | Trace execution timeline |
| `mc chat` | `--mission`, `--agent`, `--session` | Interactive agent chat |
| `mc design` | `--mission`, `--session` | Interactive workflow architect |
| `mc team list` | `--mission` | List mission team members |
| `mc team available` | — | List available catalog agents |
| `mc team add` | `AGENT_NAME`, `--mission` | Add agent to mission team |
| `mc team remove` | `MEMBER_ID`, `--mission` | Remove agent from team |
| `mc context show` | `--mission` | Show current MCD |
| `mc context history` | `--mission`, `--limit` | MCD change history |
| `mc intent show` | `--mission` | Show current intent |
| `mc intent history` | `--mission`, `--limit` | Intent revision history |
| `mc intent set` | `INTENT` (argument), `--mission`, `--scope`, `--reason` | Create new intent revision |
| `mc learnings list` | `--mission`, `--kind`, `--tags`, `--limit` | List mission learnings |
| `mc learnings show` | `LEARNING_ID`, `--mission` | Show learning detail |
| `mc knowledge search` | `QUERY` (argument), `--mission`, `--tags`, `--mode`, `--limit` | Search mission knowledge |
| `mc tree` | — | Show full CLI command tree |
| `mc console monitor` | `--mission` | Live SSE console stream |

### `admin.py` (Admin CLI)

Root options: `--verbose/-v`, `--debug/-d`, `-o/--output` (`human`|`json`|`jsonl`)

| Command Path | Options / Arguments | Purpose |
|---|---|---|
| `admin health` | — | Platform health check |
| `admin config` | — | View sanitized configuration |
| `admin info` | — | Platform information summary |
| `admin credits` | — | Credit/cost overview |
| `admin agent` | `AGENT_NAME`, `--model`, `--prompt`, `--tools` | Inspect or configure an agent |
| `admin server start` | `--host`, `--port`, `--workers`, `--reload` | Start FastAPI server |
| `admin server stop` | — | Stop running server |
| `admin server status` | — | Check server process status |
| `admin server restart` | `--host`, `--port`, `--workers` | Restart server |
| `admin telegram` | — | Telegram bot status/management |
| `admin test` | `--scope`, `--verbose` | Run test suite |
| `admin migrate upgrade` | `--revision` | Apply database migrations |
| `admin migrate downgrade` | `--revision` | Rollback database migrations |
| `admin migrate current` | — | Show current migration revision |
| `admin migrate history` | `--limit` | Show migration history |
| `admin migrate autogenerate` | `--message` | Generate new migration from model changes |
| `admin execution run` | `OBJECTIVE`, `--mission`, `--roster`, `--budget` | Start ad-hoc execution |
| `admin execution create` | `--mission`, `--objective`, `--roster` | Create execution record |
| `admin execution execute` | `EXECUTION_ID` | Execute a created record |
| `admin execution list` | `--mission`, `--status`, `--limit` | List execution records |
| `admin execution detail` | `EXECUTION_ID` | Show execution detail |
| `admin execution plan` | `EXECUTION_ID` | Show task plan for execution |
| `admin execution cost` | `EXECUTION_ID` | Show cost breakdown |
| `admin workflow list` | `--mission`, `--enabled-only` | List workflow definitions |
| `admin workflow detail` | `WORKFLOW_NAME`, `--mission` | Show workflow detail |
| `admin workflow run` | `WORKFLOW_NAME`, `--mission`, `--context` | Run a workflow |
| `admin workflow runs` | `--mission`, `--workflow`, `--status`, `--limit` | List workflow runs |
| `admin workflow run-detail` | `RUN_ID`, `--mission` | Show workflow run detail |
| `admin workflow report` | `WORKFLOW_NAME`, `--mission` | Generate workflow report |
| `admin mission create` | `--name`, `--description`, `--brief`, `--repo-url`, `--repo-root`, `--roster`, `--budget`, `--tier` | Create mission (admin) |
| `admin mission list` | `--status`, `--limit` | List all missions |
| `admin mission detail` | `MISSION` | Show mission detail |
| `admin mission archive` | `MISSION` | Archive mission |
| `admin mission delete` | `MISSION`, `--confirm` | Delete mission |
| `admin mission use` | `MISSION` | Set current mission |
| `admin mission clear` | — | Clear current mission |
| `admin mission context show` | `--mission` | Show mission context |
| `admin mission context history` | `--mission`, `--limit` | MCD change history |
| `admin mission summarize` | `--mission` | Generate mission summary |
| `admin context show` | `--mission` | Show assembled context |
| `admin context assembled` | `--mission` | Show full assembled MCD |
| `admin context codemap` | `--scope`, `--format`, `--stats` | Generate code map |
| `admin context pqi` | `--scope`, `--use-bandit`, `--use-radon`, `--with-code-map`, `--recommendations` | PQI quality score |
| `admin context deps` | — | Show dependency graph |
| `admin db stats` | — | Database statistics |
| `admin db tables` | — | List database tables |
| `admin db query` | `SQL` (argument), `--limit` | Execute raw SQL query |
| `admin db clear` | `--confirm` | Clear all data |
| `admin db clear-executions` | `--confirm`, `--mission` | Clear execution records |
| `admin db clear-sessions` | `--confirm`, `--mission` | Clear session records |
| `admin catalog list` | `--enabled-only` | List catalog entries |
| `admin catalog detail` | `AGENT_NAME` | Show catalog entry detail |
| `admin admin seed-catalog` | `--force` | Seed catalog from YAML |
| `admin admin migrate-planning-agent` | — | Migrate planning agent config |
| `admin admin seed-workflows` | `--force` | Seed workflow templates |
| `admin admin metrics` | — | Platform overview metrics |
| `admin admin costs` | `--timeframe` | Cost breakdown |
| `admin admin config` | — | View platform config |
| `admin admin health` | — | Comprehensive health check |
| `admin admin roles` | — | List configured roles |
| `admin admin user-roles` | `USER_ID` | Show user's roles |
| `admin admin update-prompt` | `AGENT_NAME`, `--content`, `--reason` | Update agent system prompt |
| `admin admin toggle-agent` | `AGENT_NAME`, `--enable/--disable` | Enable/disable catalog agent |
| `admin admin bootstrap-user` | `--email`, `--password`, `--role` | Create initial admin user |
| `admin admin seed-dev-users` | — | Seed development users |
| `admin admin reset-password` | `USER_ID`, `--password` | Reset user password |
| `admin admin ensure-system-accounts` | — | Create required system accounts |
| `admin schedule list` | `--mission`, `--enabled-only` | List workflow schedules |
| `admin schedule create` | `--workflow`, `--mission`, `--cron`, `--timezone`, `--description`, `--budget` | Create schedule |
| `admin schedule enable` | `SCHEDULE_ID` | Enable schedule |
| `admin schedule disable` | `SCHEDULE_ID` | Disable schedule |
| `admin schedule delete` | `SCHEDULE_ID` | Delete schedule |
| `admin tree` | — | Show full CLI command tree |
| `admin architect chat` | `--mission`, `--message`, `--session` | Chat with Workflow Architect |
| `admin workshop run` | `AGENT_NAME`, `--inputs`, `--prompt-override`, `--budget` | Run agent in workshop sandbox |
| `admin workshop cached-inputs` | `AGENT_NAME`, `--mission`, `--limit` | List cached workshop inputs |
| `admin workshop history` | `--agent`, `--limit` | List workshop run history |
| `admin console monitor` | `--mission` | Live SSE console stream |

---

## SSE Event Channels

| Stream Endpoint | Channel Pattern | Scope | Auth |
|---|---|---|---|
| `GET /api/v1/streams/runs/{session_id}` | `mission_events:{mission_id}` (filtered by session) | Per-session execution events | SseUser |
| `GET /api/v1/streams/notifications` | `notify:{user_id}` | Per-user notifications (approvals, completions, failures) | SseUser |
| `GET /api/v1/streams/missions/{mission_id}` | `mission_events:{mission_id}` | All events across all sessions in a mission | SseUser |
| `POST /api/v1/agents/chat/stream` | Inline queue (not Redis) | Single chat response stream | Public |
| `POST /api/v1/sessions/{id}/messages` | Inline queue (not Redis) | Single message response stream | Public |

All SSE streams emit:
- `event: connected` on initial subscription
- `: keepalive` comments for disconnect detection
- `event: done` sentinel on completion (inline streams only)

---

## Router Mounting Summary

```
FastAPI app
├── /health, /health/ready, /health/detailed    ← health.router (no prefix)
└── /api/v1                                      ← api_v1_router
    ├── /agents                                  ← agents.router
    ├── /catalog/agents                          ← catalog.router
    ├── /sessions                                ← sessions.router
    ├── /executions                              ← executions.router
    ├── /missions                                ← missions.router
    │   ├── (team sub-router)                    ← missions_team.router
    │   ├── (workflows sub-router)               ← missions_workflows.router
    │   ├── (context sub-router)                 ← missions_context.router
    │   ├── (plan sub-router)                    ← missions_plan.router
    │   ├── (artifacts sub-router)               ← missions_artifacts.router
    │   └── (costs sub-router)                   ← missions_costs.router
    ├── /missions/{id}/knowledge|learnings|intent ← mission_knowledge.router (root-mounted)
    ├── /knowledge                               ← knowledge.router (root-mounted)
    ├── /workflows                               ← workflows.router
    ├── /workshop                                ← workshop.router
    ├── /auth                                    ← auth.router
    ├── /admin                                   ← admin.router
    ├── /admin/users                             ← admin_users.router
    ├── /streams                                 ← streams.router
    └── /client-errors                           ← client_errors.router
```

---

## Source Files

| File | Endpoint Group | Line Count |
|------|---------------|------------|
| `modules/backend/api/health.py` | Health checks | ~229 |
| `modules/backend/api/v1/endpoints/auth.py` | Authentication | ~263 |
| `modules/backend/api/v1/endpoints/agents.py` | Agent chat & registry | ~206 |
| `modules/backend/api/v1/endpoints/catalog.py` | Agent catalog browse | ~69 |
| `modules/backend/api/v1/endpoints/sessions.py` | Session management | ~363 |
| `modules/backend/api/v1/endpoints/executions.py` | Execution records | ~442 |
| `modules/backend/api/v1/endpoints/missions.py` | Mission CRUD + narrative | ~243 |
| `modules/backend/api/v1/endpoints/missions_team.py` | Team management | ~341 |
| `modules/backend/api/v1/endpoints/missions_plan.py` | Plan management | ~487 |
| `modules/backend/api/v1/endpoints/missions_context.py` | MCD & console history | ~106 |
| `modules/backend/api/v1/endpoints/missions_artifacts.py` | Artifact listing | ~101 |
| `modules/backend/api/v1/endpoints/missions_costs.py` | Cost breakdown | ~74 |
| `modules/backend/api/v1/endpoints/missions_workflows.py` | Workflows, schedules, architect, human input | ~739 |
| `modules/backend/api/v1/endpoints/mission_knowledge.py` | Mission-scoped knowledge | ~199 |
| `modules/backend/api/v1/endpoints/knowledge.py` | Platform knowledge (admin) | ~122 |
| `modules/backend/api/v1/endpoints/workflows.py` | Legacy workflow/execution | ~205 |
| `modules/backend/api/v1/endpoints/workshop.py` | Agent sandbox | ~124 |
| `modules/backend/api/v1/endpoints/streams.py` | SSE streaming | ~258 |
| `modules/backend/api/v1/endpoints/client_errors.py` | Frontend error reporting | ~48 |
| `modules/backend/api/v1/endpoints/admin.py` | Admin operations | ~792 |
| `modules/backend/api/v1/endpoints/admin_users.py` | Admin user management | ~255 |
