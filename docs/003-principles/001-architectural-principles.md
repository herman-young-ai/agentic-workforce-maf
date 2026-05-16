# Architectural Principles

**Version:** 1.1
**Date:** 2026-05-12
**Classification:** Internal
**Foundation:** [Steenberg Principles](../098-research/000-steenberg_principles.md), [Mission Control Reference Architecture](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md), validated against prototype

This document defines the non-negotiable architectural principles for the Agentic Workforce Platform. Every design decision must be checked against these principles.

---

## 1. The Unifying Primitive: Task

### What We Validated

We stress-tested five candidate primitives (Task, Event, Project, WorkUnit, ProjectEntry) against Steenberg's six characteristics (generality, simplicity, consistency, completeness, implementability, extensibility) and validated against the Mission Control prototype codebase.

**Finding:**

| Concept | Role | Analogy |
|---------|------|---------|
| **Project** | The scope — scopes everything, owns budget/context/team/lifecycle | Unix directory, video editor timeline |
| **Task** | The primitive — the one thing the whole system manipulates | Unix file, video editor clip |
| **Session** | Transport — how conversations happen, not a competing primitive | A pipe, not a file |

**Project is the scope. Task is the primitive. Session is transport.**

### What "Task Is the Primitive" Means

Every significant operation in the system is a Task:

| Operation | As a Task |
|-----------|----------|
| An agent scanning code for vulnerabilities | Task: type=agent_task, agent=security.reviewer |
| A human approving scan findings | Task: type=human_decision, assigned_to=reviewer |
| An AI classifying finding severity | Task: type=ai_decision, agent=quality.verifier |
| A workflow node executing | Task: created from workflow node definition |
| An ad-hoc `awp run "..."` | Task: type=agent_task, source=ad_hoc |
| A planner creating a plan | Task: type=agent_task, agent=planner (produces child tasks) |
| A Kanban card | Visual representation of a Task |
| A retried failed execution | Task: same definition, new attempt |

Everything else is a **by-product** of task execution:
- Tasks produce **Artifacts**
- Tasks generate **Events**
- Tasks surface **Learnings**
- Tasks update **Context (PCD)**
- Tasks make **Decisions**

### Task Must Be First-Class

The prototype's critical flaw: tasks existed as JSON schemas inside `task_plan_json` and were shadowed in `plan_task_states`. They lacked proper relational identity — you couldn't FK to a task, query tasks independently, or track a task's lifecycle across retries.

**In our platform, Task is a first-class relational entity:**

```
Task (the primitive)
├── id (UUID, PK)
├── project_id (FK → Project, the scope)
├── type (agent_task | human_decision | ai_decision | action | sub_workflow)
├── status (proposed | approved | queued | running | completed | failed | skipped | cancelled)
├── objective (what this task achieves)
├── agent_name (which agent runs it, null for human tasks)
├── source (workflow | planner | manual | ad_hoc | retry)
├── workflow_node_id (if created from a workflow node)
├── parent_task_id (FK → Task, for sub-tasks and nesting)
├── dependencies[] (other task IDs that must complete first)
├── inputs (JSON — data from upstream tasks or user)
├── outputs (JSON — what the task produced)
├── output_summary (one-line for list views)
├── cost_usd, duration_seconds
├── retry_count, max_retries
├── assigned_to (user ID for human decisions)
├── session_id (FK → Session, for approval conversations)
├── created_by (agent or human who created this task)
├── created_at, started_at, completed_at
│
└── Relationships (everything hangs off Task):
    ├── → TaskAttempts[] (execution attempts with pass/fail)
    ├── → Artifacts[] (produced by this task)
    ├── → Learnings[] (extracted from this task's output)
    ├── → Events[] (generated during this task)
    └── → Decisions[] (made during this task)
```

**The Kanban board is the Task table rendered visually. The workflow engine creates Tasks. The context assembler reads Tasks. The audit pipeline records Tasks. The budget tracker costs Tasks.**

### No Hidden Side-Effects — Make System Work Visible

The prototype hid two important operations as invisible side-effects: PCD updates and knowledge extraction ran silently after task completion. In our platform, these are **visible tasks** on the Kanban board:

| Operation | Prototype | Our Design |
|-----------|-----------|------------|
| Planning | Direct agent call in orchestrator | Task type=`agent_task`, agent=planner. Produces child tasks. Visible on board. |
| Human approval | Separate `HumanInputRequest` entity | Task type=`human_decision`. Same lifecycle, same board, same audit. |
| AI evaluation | Implicit Temporal activity | Task type=`ai_decision`. Constrained output schema. Visible on board. |
| PCD update | Silent side-effect in `KnowledgeExtractor` | Task type=`action`. Updates PCD, visible on board, has cost, can fail and retry. |
| Knowledge extraction | Silent side-effect after execution | Task type=`action`. Runs Haiku agent, extracts learnings, visible on board. |

**Why this matters:** If it has cost, can fail, or should be auditable — it's a Task. Making system work visible means operators can see exactly what the platform is doing, not just what agents are doing.

---

## 2. Project Is the Scope

Everything is scoped to a Project. A Project is NOT a primitive — it's the container that gives Tasks meaning.

**What Project owns:**
- Budget (all task costs roll up to project)
- Context (PCD — accumulated knowledge)
- Team (which agents are available)
- Members (which humans participate)
- Lifecycle (active, paused, archived)
- Jurisdiction (data residency rules)
- Guardrails (constraints tasks must respect)

**What Project does NOT do:**
- Project is not executed (tasks are)
- Project is not approved (tasks are)
- Project is not retried (tasks are)
- Project does not have an agent (tasks do)

**Scoping rule:** Every entity in the system has a `project_id` FK, except platform-level entities (users, agent catalog, platform knowledge, model pricing).

---

## 3. Session Is Transport

Sessions are how conversations happen — between humans and agents, or during approval flows. Sessions are NOT a competing primitive.

**Sessions connect to Tasks:**
- A chat session is associated with a project (and optionally a specific task context)
- An approval conversation happens in a session linked to a human_decision task
- Session memory (rolling summary) serves the conversation, not the project's knowledge

**Sessions do NOT own knowledge.** Learnings are extracted from task outputs, not from sessions. PCD is updated by the knowledge extractor after task completion, not by the session.

---

## 4. Wrap the Core

**Principle:** Build a system that others plug into, not a system that plugs into a larger framework. Never call external code directly from application logic.

### What We Wrap

| External Dependency | Our Wrapper Interface | Why |
|--------------------|-----------------------|-----|
| MAF (`ChatClientAgent`, `AIAgent`, `AgentSession`) | `IAgentRuntime` | MAF is v1.5, will change. Our interface is stable for 10+ years. |
| Durable Task SDK | `IWorkflowEngine` | Durability backend may change (DTS → Temporal → something new). |
| Azure AI Foundry / Anthropic SDK | `IModelProvider` (via `IChatClient` pipeline) | Model providers will change. Already designed to swap. |
| EF Core / Npgsql | Repository interfaces (`IProjectRepository`, `ITaskRepository`, `IKnowledgeStore`) | ORM version churns yearly. DB engine may change. |
| SignalR | `IEventPublisher` | Real-time transport may change (SignalR → Web PubSub → custom). |
| Azure Blob Storage | `IDocumentStore`, `IArtifactStore` | Storage backend may change. |
| Azure Event Hubs | `IAuditSink` | Audit transport may change. |

### Rules

1. **Application code (Api, Worker) never imports external library namespaces directly.**
2. **Only wrapper projects (Agents, Infrastructure) know about MAF, EF Core, Azure SDKs.**
3. **Switching a provider requires changing ONE wrapper class, not the application.**
4. **Every wrapper interface has at least two implementations: production + test/mock.**

### Dependency Flow

```
Api ──────────→ Interfaces (our contracts)
Worker ────────→ Interfaces
                    ↑
Agents ─────────── implements IAgentRuntime (wraps MAF)
Infrastructure ─── implements IProjectRepository (wraps EF Core)
                   implements IEventPublisher (wraps SignalR)
                   implements IDocumentStore (wraps Blob Storage)
```

**No skipping layers. No reaching into internals. No importing external types in application code.**

---

## 5. Perfect APIs, Simple Implementations

**Principle:** Design the ideal API from the start. Implementations can be simple placeholders. The API is permanent; the implementation is provisional.

### Applied

- Design internal service interfaces (`IAgentRuntime`, `IWorkflowEngine`, `IKnowledgeStore`) for 10+ year stability
- Include parameters for future features even if unimplemented today
- Document contracts, not implementation details
- Make invalid use impossible (fail fast, not silent corruption)

### Example

```csharp
// This interface is permanent — designed today, stable for 10 years
public interface IAgentRuntime
{
    Task<TaskResult> ExecuteAsync(
        Task task,                    // the primitive
        ProjectContext context,       // the scope
        AgentConfig agent,            // which agent
        ExecutionOptions options,     // future-proofed options
        CancellationToken ct);

    IAsyncEnumerable<TaskEvent> ExecuteStreamingAsync(
        Task task,
        ProjectContext context,
        AgentConfig agent,
        ExecutionOptions options,
        CancellationToken ct);
}

// This implementation is provisional — wraps MAF today, could wrap anything tomorrow
internal class MafAgentRuntime : IAgentRuntime
{
    // Uses ChatClientAgent, IChatClient, AIContextProvider internally
    // External code never sees MAF types
}
```

---

## 6. Formats Are Everything

**Principle:** Data formats are the most critical, long-term contracts. They outlive implementations.

### Our Critical Formats

| Format | What It Stores | Longevity |
|--------|---------------|-----------|
| **PCD** (Project Context Document) | Accumulated project knowledge | Years — agents read/write this continuously |
| **Workflow Definition** | Nodes + edges + conditions | Years — processes are defined once, run many times |
| **Task Definition** | Objective, agent, inputs, dependencies | Core primitive format — must be stable |
| **Learning** | Discovered pattern with confidence/evidence | Years — knowledge accumulates indefinitely |
| **Event** | Console/audit event with type and payload | Years — audit trail is 7-year retention |

### Rules

1. **Every stored format includes `format_version`** — enables migration when formats evolve
2. **The weekend test** — can someone implement a parser for this format in a weekend? If not, simplify.
3. **Backward compatibility is mandatory** — v2 readers must handle v1 documents
4. **Separate semantics from structure** — domain meaning (what) is stable; encoding (JSON, Protobuf) can change
5. **Never expose storage engines in APIs** — callers never send SQL, never see EF Core types

### Format Versioning

```json
{
  "format_version": "1.0",
  "identity": { "name": "Payments Security Q2", ... },
  "principles": [...],
  "guardrails": [...],
  "current_state": {...}
}
```

When PCD format v2 adds a new section, the migration service reads v1 documents and upgrades them transparently.

---

## 7. Modules Are Black Boxes

**Principle:** Communication only via stable APIs. A module can be completely rewritten without breaking dependents.

### Verification Questions

For every project/module in the solution:
- Can it be completely rewritten without breaking dependents? **Must be YES.**
- Do consumers need to understand its implementation? **Must be NO.**
- Is internal state completely hidden? **Must be YES.**

### Module Sizing

Target 500-5000 lines per module. If a module exceeds 5000 lines, split it. If it's under 500, consider merging with a related module.

### One Owner Per Module

Each module has a single owner who understands it completely. When the owner leaves, the module can be rewritten (because it's a black box with a stable API).

---

## 8. Fail Fast, Never Degrade Silently

**Principle:** Explicit failure beats hidden recovery. Crashes with great diagnostics get fixed; silent misbehaviour lingers.

### Applied

| Situation | Wrong | Right |
|-----------|-------|-------|
| Budget exceeded | Silently downgrade to cheaper model | **Throw `BudgetExceededException` immediately** |
| PCD mutation on restricted path | Silently ignore | **Throw `RestrictedPathException`** |
| Agent produces malformed output | Silently accept | **Throw validation error, record in audit** |
| Hash chain sequence gap | Silently skip | **Halt audit pipeline, alert immediately** |
| Invalid task dependency | Silently execute anyway | **Throw `DependencyNotMetException`** |

### Debug vs Release

- **Debug mode:** Full prompts visible in console, token counts on every message, PCD validator runs on every mutation, sentinel GUIDs (`Guid.Empty`) crash if they reach DB
- **Release mode:** Summaries in console, validators run but log instead of crash, sentinels still crash (this is always a bug)

---

## 9. Log Everything, Blacklist Noise

**Principle:** Every event is logged by default. Exclude noisy events via a configurable blacklist — never whitelist. Errors are NEVER blacklisted.

### Applied

- Dual persistence: JSONL file (synchronous safety net) + DB (queryable store)
- Console view streams everything in real-time via SignalR
- Blacklist is per-project and configurable (operators can toggle for debugging)
- Errors always surface immediately — red, loud, and unmissable

---

## 10. No Model Downgrade, Ever

**Principle:** Accuracy and predictability are paramount. When budget is exceeded, execution fails fast and loud. A wrong answer from a cheaper model is worse than a stopped execution.

### Applied

- Budget warnings at 80% notify users proactively via SignalR
- At 100%: `BudgetExceededException` — execution terminates immediately
- No silent model substitution, no transparent quality degradation
- Users can extend the budget and retry — the choice is theirs, not the system's

---

## 11. Segregation of Duties

**Principle:** The person who triggers an execution cannot approve its result.

### Applied

- **Reviewer** role exists specifically for approval without execution rights
- Enforcement: `triggered_by != approved_by` on all approval gates
- Cannot be bypassed by project configuration — it's a platform-level invariant

---

## 12. Plugin Architecture

**Principle:** Features via plugins, not core bloat. The core is minimal; everything else plugs in.

### What's a Plugin

| Plugin Type | Self-Describing? | Discoverable? | Core Changes to Add? |
|------------|------------------|---------------|---------------------|
| Agent | Yes (catalog entry with tools, model, constraints) | Yes (agent catalog API) | No |
| Template | Yes (composition rules, approval flows, guardrails) | Yes (template API) | No |
| Tool (AIFunction) | Yes (description, parameters, return type) | Yes (tool registry) | No |
| Tool (MCP) | Yes (MCP tool listing) | Yes (MCP discovery) | No |
| Workflow Node Type | Yes (node schema with inputs/outputs/config) | Yes (node type registry) | No |
| Document Extractor | Yes (supported MIME types) | Yes (extractor registry) | No |
| Notification Channel | Yes (channel type, config schema) | Yes (channel registry) | No |

**The plugin validation test:** If adding a new agent, tool, template, or node type requires changing the core, the plugin architecture isn't general enough.

---

## 13. Retract, Don't Delete

**Principle:** Knowledge is never truly deleted — it's retracted. Audit trail is preserved.

### Applied

- Wrong learnings: status → `retracted`, excluded from context, visible (greyed out) in Knowledge View
- Superseded learnings: old learning links to corrected version
- Only Platform Admins can permanently purge (for GDPR/data retention)
- PCD changes recorded in change history with old/new values

---

## 14. Secure by Default (The Unix Model)

**Principle:** All external interfaces start in a denied state and require explicit configuration to open. When a security-relevant configuration is missing, empty, or ambiguous, the system denies access — it never silently degrades to permissiveness. This is the Linux model (no permissions until granted), not the Windows model (full access until restricted).

*Adopted from: [Mission Control Reference Architecture P8](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

| Situation | Wrong (Windows model) | Right (Unix model) |
|-----------|----------------------|-------------------|
| Empty allowlist | Allow everyone | **Deny all** |
| New feature | Enabled by default | **Disabled until explicitly enabled** |
| New API endpoint | Public until secured | **Authenticated until explicitly exempted** |
| New notification channel | Open until restricted | **Closed until explicitly opened** |
| Missing webhook secret | Accept unsigned payloads | **Refuse to mount the endpoint** |
| CORS in production | Permissive until configured | **Strict by default, explicit origins only** |
| Debug mode in production | On until someone notices | **Rejected at startup** |
| Agent tool access | All tools available | **Only tools in the manifest are callable** |
| PCD write paths | All paths writable | **Only explicitly allowlisted paths** |
| Network egress (sandbox) | Open by default | **Disabled by default (Azure Firewall allowlist)** |

**Relationship to other principles:**
- Principle 8 (Fail Fast) says crash when config is missing.
- This principle says even when config IS present, the default posture must be closed.

---

## 15. Backend Owns All Business Logic

**Principle:** Every business rule, validation, calculation, and decision lives in the backend. Clients display results; they do not compute them.

*Adopted from: [Mission Control Reference Architecture P1](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

**Backend (always):** Data validation, authentication, authorisation, cost calculations, task state transitions, agent execution decisions, workflow routing, PCD mutations, learning extraction, budget enforcement.

**Client (only):** UI state (tab selection, modal open/close), optimistic UI updates, input formatting (display only), navigation/routing, rendering/layout.

**When uncertain, put it in the backend.** The cost of a client being too thin is a few extra API calls. The cost of a client being too thick is duplicated logic across every client surface (React SPA, CLI, Telegram, API consumers, future mobile).

This is critical for the Agentic Workforce Platform because agents are also API consumers — they interact via the same backend services. Business logic in the frontend would be invisible to agents.

---

## 16. Single Source of Truth Per Entity

**Principle:** One authoritative write source per data type. Read replicas, caches, and analytical copies derive from the authoritative source, never the reverse.

*Adopted from: [Mission Control Reference Architecture P3](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

| Data Type | Authoritative Source | Derived Copies |
|-----------|---------------------|---------------|
| All domain entities (Project, Task, etc.) | PostgreSQL | Redis cache (ephemeral, reconstructable) |
| Audit records | Blob Storage WORM + Eventhouse | PostgreSQL `project_events` (operational view) |
| Session messages | PostgreSQL | Redis (SignalR backplane relay) |
| Embeddings | PostgreSQL (pgvector) | None |
| LLM call records | PostgreSQL (partitioned) | Eventhouse (analytics copy) |
| Agent catalog | PostgreSQL | In-memory cache (5-min TTL, Redis pub/sub eviction) |
| Project role cache | PostgreSQL `project_members` | IMemoryCache (5-min, evicted on change) |

**Rule:** Multiple read technologies are fine; multiple write sources for the same entity are not. If Redis goes down, the system rebuilds from PostgreSQL. If PostgreSQL goes down, the system stops — it doesn't fall back to Redis as the source of truth.

---

## 17. Human Authority

**Principle:** Humans can override any decision an agent makes. Respect kill switches and approval gates. When uncertain, escalate rather than guess.

*Adopted from: [Mission Control Organization Principles §4](https://github.com/investec/mission-control/config/prompts/organization/principles.md)*

### Applied

- **Kill switches:** Emergency stop (platform-wide or project-scoped) halts all autonomous execution instantly
- **Approval gates:** Human Decision nodes in workflows pause execution until a human explicitly approves
- **Knowledge retraction:** Humans can retract any learning an agent discovered — the agent's "knowledge" is advisory, not authoritative
- **PCD principles:** Human-authored direction overrides agent-discovered patterns — principles are never trimmed from context
- **Budget override:** Humans can extend budgets when hit — the system fails fast but the human decides what happens next
- **Agent output:** Every agent output is a proposal, not a commitment — artifacts, PCD updates, and learnings all surface for human review

**Escalation rule for agents:** When an agent is uncertain about a decision (confidence < threshold, ambiguous instructions, conflicting constraints), it must escalate to a human via a `human_decision` task rather than guessing. Wrong answers are more expensive than delayed answers.

---

## 18. Idempotency for All Operations

**Principle:** Operations can be safely retried. Duplicate requests produce the same result as single requests. This enables reliable recovery from network failures, pod restarts, and Durable Task replays.

*Adopted from: [Mission Control Reference Architecture P6](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

- Create operations check for existing records before inserting
- Update operations use optimistic concurrency (`xmin` version check)
- Delete operations succeed even if already deleted (idempotent 204)
- Critical mutations accept `Idempotency-Key` header — response cached in Redis for 24h
- Durable Task activities are idempotent by design (replay-safe)
- Knowledge deduplication (cosine > 0.92) is a form of idempotent knowledge storage

---

## 19. Bounded Resource Usage

**Principle:** All operations have timeouts. All queries have limits. All queues have maximum sizes. Unbounded operations do not exist.

*Adopted from: [Mission Control Reference Architecture O3](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

| Resource | Bound | Enforcement |
|----------|-------|-------------|
| API request body | 10 MB (50 MB for file uploads) | Kestrel limits |
| API pagination | Max 100 per page | Query parameter validation |
| Agent tool calls per execution | Configurable ceiling (default 50) | Tool call counter middleware |
| Shell command execution time | 5 minutes per command | Dynamic Sessions timeout |
| Task execution time | 30 minutes (configurable) | Durable Task activity timeout |
| Workflow execution time | 30 days | Durable Task orchestration timeout |
| Human approval timeout | 4 hours (default), 24 hours (escalation) | WaitForExternalEvent timeout |
| Session message count before compression | 50 messages | Rolling summary compression |
| Context assembly token budget | ~100K tokens | ContextAssembler priority trimming |
| Cost per agent per execution | $1.00 (configurable) | BudgetEnforcingChatClient |
| Rate limits (LLM-triggering) | 10 req/min | AddRateLimiter middleware |
| Channel audit buffer | 50,000 records | Bounded Channel (Wait + 5s timeout → AuditBackpressureException) |

**If a resource has no bound, it's a bug.** Add a bound, even if the initial value is generous.

---

## 20. Version Everything

**Principle:** APIs are versioned. Formats are versioned. Schemas are versioned. Breaking changes are never introduced without version increment.

*Adopted from: [Mission Control Reference Architecture D4](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

| What | Versioned How |
|------|--------------|
| REST API | URL prefix (`/api/v1/`) with `Sunset` header for deprecation |
| PCD format | `format_version` field on entity |
| Workflow definition | `format_version` + `version` (integer, incremented on edit) |
| Task format | `format_version` field |
| Learning format | `format_version` field |
| Agent system prompt | `PromptVersion` table with revision chain |
| Database schema | EF Core migrations with rollback scripts |
| Agent catalog | `version` field on `AgentCatalog` entity |

**Backward compatibility is mandatory.** v2 readers must handle v1 documents. Old formats are migrated transparently, never rejected.

---

## 21. Explicit Over Implicit

**Principle:** Configuration is explicit. Dependencies are declared. Side effects are documented. Nothing happens "magically."

*Adopted from: [Mission Control Reference Architecture P4](https://github.com/investec/mission-control/docs/99-reference-architecture/01-core-principles.md)*

### Applied

- No auto-discovery of agents or services — everything registered explicitly in DI or catalog
- No implicit type coercion — all JSON properties have explicit `[JsonPropertyName]` attributes
- No hidden global state — `ProjectContext` is passed explicitly (not leaked via ambient state)
- All environment variables documented in configuration reference
- All side effects of task execution are visible Tasks on the Kanban board (Principle 1: no hidden side-effects)
- Agent tool manifests are explicit — agents cannot discover or call tools not in their manifest

---

## 22. Container-First Tool Execution

**Principle:** All tool execution that interacts with external systems, executes code, accesses filesystems, or makes network calls runs inside a container (ACA Dynamic Sessions) by default. Only internal platform query tools — tools that read/write our own PostgreSQL database through our own service interfaces — are exempt and run in-process.

*Informed by: [BFA Reference Architecture — Execution Environments](../../bfa_reference_architecture/docs/99-reference-architecture/45-agentic-execution-environments.md), Principle 14 (Secure by Default)*

### Why This Matters

The BFA prototype ran all tools `local` by default, relying on `FileScope` (logical, software-level guards) for isolation. A bug in FileScope, a new tool that forgot the check, or a prompt injection that steered tool arguments could bypass logical isolation entirely. The container boundary is an OS-level enforcement — it cannot be bypassed by application-level bugs.

More critically: tools that make outbound HTTP calls (web search, SonarQube, Snyk) running in-process in the Worker bypass all sandbox network egress controls. A prompt-injected agent calling an in-process `web.search` tool can make arbitrary HTTP requests from the Worker's network identity. Containerized, that same tool is constrained by the Azure Firewall egress allowlist.

### Two Execution Domains

| Domain | Where | Tools | Rationale |
|--------|-------|-------|-----------|
| **Platform** (in-process) | Worker process | `project.*` tools (get_info, get_pcd, get_history, get_team, get_learnings, get_artifacts, get_plan, list_workflows, approve_tasks, refine_plan, run_objective, run_workflow, add_principle, update_budget, get_recent_outcomes, get_past_decisions) | Read/write our own DB through our own service interfaces. No external interaction. No network calls. No file I/O. |
| **Sandbox** (containerized) | ACA Dynamic Sessions | ALL other tools: `file.*`, `shell.*`, `web.*`, `git.*`, `security.*`, `research.*`, `software.*`, any tool that makes HTTP calls to external services (Tavily, SonarQube, Snyk, Azure AI Search, SharePoint, compliance APIs) | Default domain. Any external interaction requires sandbox network egress controls. |

### Applied

| Tool | Old Classification | New Classification | Why |
|------|-------------------|-------------------|-----|
| `web.search` (Tavily/Brave/Perplexity) | In-process AIFunction | **Sandbox** (Dynamic Sessions) | Makes outbound HTTP to third-party APIs. Must go through egress allowlist. |
| `web.fetch` | In-process AIFunction | **Sandbox** (Dynamic Sessions) | Fetches arbitrary URLs. Must be egress-constrained. |
| SonarQube, Snyk API | In-process AIFunction | **Sandbox** (Dynamic Sessions) | External REST API calls. Must go through egress allowlist. |
| Azure AI Search, SharePoint/Graph | In-process AIFunction | **Sandbox** (Dynamic Sessions) | Azure SDK calls that cross the network boundary. Containerized so egress is explicit. |
| `file.read`, `file.write`, `file.search` | Dynamic Sessions | **Sandbox** (Dynamic Sessions) | Already correct — no change. |
| `shell.execute` | Dynamic Sessions | **Sandbox** (Dynamic Sessions) | Already correct — no change. |
| `security.code.scan`, `security.deps.scan` | MCP in container | **Sandbox** (MCP in container) | Already correct — no change. |
| `project.get_info`, `project.get_pcd`, etc. | In-process | **Platform** (in-process) | Internal DB queries. No external interaction. Exempt from containerization. |

### Enforcement

1. **`ToolRegistry` enforces execution domain at registration.** Every tool is registered with an `ExecutionDomain` (Platform or Sandbox). Sandbox tools receive an `IDynamicSessionClient`; Platform tools receive service interfaces directly.
2. **New tools default to Sandbox.** Adding a tool without specifying a domain defaults to containerized execution (Principle 14: Secure by Default). Platform domain requires explicit registration with justification.
3. **Architecture tests verify domain assignments.** A test asserts that no tool registered as Platform makes outbound HTTP calls or accesses the filesystem.

---

## Decision Checklist

Before finalising any architectural decision, verify:

- [ ] Does it treat Task as the primitive and Project as the scope?
- [ ] Does it isolate external dependencies behind our own interfaces?
- [ ] Is the API simpler than the implementation?
- [ ] Can the module be completely rewritten without breaking dependents?
- [ ] Is the format dead simple (weekend test)?
- [ ] Does it fail fast and loud on errors?
- [ ] Does it log by default and blacklist noise?
- [ ] Can new capabilities be added without core changes (plugin test)?
- [ ] Will this still be maintainable in 10 years?
- [ ] Does it preserve audit trail (retract, don't delete)?
- [ ] Is the default posture closed/denied (secure by default)?
- [ ] Does business logic live in the backend, not the client?
- [ ] Is there exactly one write source for this data?
- [ ] Can a human override this decision?
- [ ] Is this operation idempotent (safe to retry)?
- [ ] Does every resource have a bound (timeout, limit, max size)?
- [ ] Is this change versioned?
- [ ] Is the configuration explicit (no magic)?
- [ ] Does this tool run in a container? (If it touches the network or filesystem, it must.)
