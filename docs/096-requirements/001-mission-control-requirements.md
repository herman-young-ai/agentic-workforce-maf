# Software Requirements Specification: Mission Control

**Document Status:** Populated
**Version:** 1.0
**Classification:** Internal
**Date:** 2026-05-10

---

## Agent Instructions

This document is a structured template for extracting software requirements from the Mission Control codebase. The goal is to produce a **framework-neutral, implementation-agnostic** requirements specification that can be used as the basis for reimplementation in any language or framework.

**Extraction Rules:**

1. Capture **what** the system does, not **how** it does it in the current implementation.
2. Where the codebase uses framework-specific patterns (e.g. decorator-based agent definitions, specific ORM patterns, framework validation models), extract the underlying requirement, not the mechanism.
3. Record current implementation details only in the `Source Reference` and `Implementation Notes` fields. These exist for traceability, not as requirements.
4. Every functional requirement must be testable. If you cannot define a pass/fail condition, the requirement is too vague.
5. Preserve all domain terminology exactly as used in the codebase (Mission, MCD, Session, Agent, Workflow, Gate, etc.). These are domain primitives, not implementation choices.
6. Where you find hardcoded values (timeouts, limits, thresholds, retry counts), capture them as configurable parameters with the current value noted as the default.

**Extraction Process:**

1. Start with the project structure. Map every module, package, and significant file.
2. Identify the domain model from data classes, schemas, database models, and type definitions.
3. Trace the primary execution flows end-to-end (mission lifecycle, agent orchestration, session management).
4. **Map the memory and context architecture:** context assembly pipeline, MCD lifecycle, session memory compression, knowledge surfaces, prompt layering, token budget management. This is the mechanism that determines what agents know and remember — treat it as a first-class extraction target, not an afterthought.
5. Extract API contracts from route definitions, request/response schemas, and SSE/streaming handlers.
6. Identify integration points: databases, message brokers, external APIs, LLM providers.
7. Extract error handling patterns, retry logic, and failure modes.
8. Capture configuration, environment variables, and feature flags.
9. Document security controls: authentication, authorisation, input validation, secrets management.
10. Review test files for implicit requirements not obvious from production code.
11. **Document the gate/approval system:** modes, stages, escalation paths, client-surface rendering.
12. **Document the cost tracking pipeline:** LLM call recording, budget enforcement, token economics, alerting.

---

## 1.0 Purpose and Scope

### 1.1 System Purpose

> Extract from: README, docstrings, top-level module comments, CLI help text.

**System Name:** Mission Control

**One-line Description:** An AI-first autonomous agent platform that orchestrates teams of specialised AI agents to execute long-running missions against codebases and projects, with durable workflow execution, human-in-the-loop approval gates, and accumulated knowledge across runs.

**Problem Statement:** Complex software engineering and research tasks require coordinated effort from multiple AI agents with different specialisations (planning, coding, security, research, architecture review). These agents need persistent context that survives across sessions, budget controls, quality verification, and human oversight at configurable levels of autonomy. Mission Control provides the orchestration layer, knowledge platform, and multi-channel interface to make long-lived autonomous agent missions tractable and safe.

**Target Users:**

| User Type | Interface | Usage |
|-----------|-----------|-------|
| Software engineers / project owners | CLI (`mc.py`), React frontend, TUI | Run objectives, chat with agents, review results, manage missions |
| Platform administrators | CLI (`admin.py`), admin API endpoints | Server lifecycle, DB management, agent catalog, user management, workflow seeding |
| AI agents (internal) | Service layer, tool system, event bus | Execute tasks, read/write files, search web, query knowledge, schedule followups |
| External systems | REST API, Telegram webhook, SSE streams | Programmatic mission execution, notifications, real-time event consumption |

### 1.2 System Boundaries

**In Scope:**
- Agent orchestration (planning, dispatch, execution, verification, retry)
- Mission lifecycle management (creation, context accumulation, archival, deletion)
- Multi-channel client interfaces (CLI, TUI, React frontend, Telegram, REST API)
- Knowledge platform (MCD, intent, learnings, decisions, milestones, embeddings, search)
- Durable workflow execution via Temporal (mission runs, chat turns, scheduled jobs, human input)
- Cost tracking and budget enforcement per agent, execution, and mission
- Human-in-the-loop approval gates (plan, task, post-run)
- Session management with rolling summary compression
- Agent catalog with versioned prompts and configurable tool/scope/model assignments
- Execution environment isolation (local, worktree, container modes)
- Security: JWT/API key auth, RBAC, FileScope, input guardrails, idempotency

**Out of Scope:**
- LLM inference (delegated to external providers: Anthropic, OpenAI, GitHub Copilot)
- Database hosting (PostgreSQL managed externally)
- Redis hosting (managed externally)
- Temporal server hosting (run separately or as dev-mode embedded)
- Docker/OrbStack runtime (prerequisite for container-mode execution)
- Frontend hosting (Vite dev server or static deployment)
- Web search providers (Perplexity, Tavily, Brave — consumed via API keys)

**Client Surfaces:**

| Surface | Type | Entry Point | Description |
|---------|------|-------------|-------------|
| User CLI | Click CLI (thin HTTP client) | `mc.py` | Run objectives, chat, design workflows, manage missions/team/context |
| Admin CLI | Click CLI (mixed thin + direct service) | `admin.py` | Server lifecycle, DB ops, migrations, agent/workflow/execution management |
| TUI | Textual terminal app (in-process) | `tui.py` | Interactive dashboard with real-time event streaming and gate integration |
| Service Manager | argparse CLI | `services.py` | Start/stop/restart backend, frontend, Temporal; preflight checks |
| REST API | FastAPI (JSON over HTTP) | `modules/backend/main.py` | Full CRUD for missions, sessions, executions, workflows, catalog, admin |
| SSE Streams | Server-Sent Events (Redis pub/sub) | `/api/v1/streams/` | Real-time mission events, console history, notifications |
| Telegram | Webhook + polling bot (aiogram v3) | `modules/telegram/` | Notifications, chat, approvals via Telegram |
| React Frontend | Vite + React + Tailwind | `modules/frontend/` | Web-based mission dashboard |

### 1.3 Document Conventions

- **FR-XXX:** Functional Requirement
- **NFR-XXX:** Non-Functional Requirement
- **DR-XXX:** Data Requirement
- **IR-XXX:** Integration Requirement
- **SR-XXX:** Security Requirement

---

## 2.0 System Context

### 2.1 Architecture Overview

> Extract from: docker-compose.yml, infrastructure configs, service dependencies, main application factory.

**Deployment Topology:** Co-located development deployment. Five processes managed by `services.py`: FastAPI backend (uvicorn), Temporal dev server, Temporal worker, Vite frontend dev server, and optionally Docker/OrbStack for container-mode agent execution. PostgreSQL and Redis are external prerequisites.

**Runtime Dependencies:**

| Component | Role | Current Implementation | Required Capability |
|-----------|------|----------------------|-------------------|
| Primary Database | Persistent storage for all domain entities | PostgreSQL (asyncpg + SQLAlchemy async) | Relational store with JSON columns, UUID primary keys, timestamptz, pgvector extension for embeddings, full ACID transactions |
| Cache / Event Transport | Event pub/sub, SSE delivery, session state, idempotency cache | Redis (redis-py async) | In-memory store with pub/sub channels, key expiry (TTL), atomic getdel for single-use tokens |
| Workflow Engine | Durable execution of mission runs, chat turns, scheduled jobs, human input pauses | Temporal (temporalio Python SDK) | Durable workflows with signals, queries, child workflows, scheduled execution, retry policies, activity timeouts, parent close policies |
| LLM Provider (primary) | Agent inference — chat completion, structured output, tool calling | Anthropic API (anthropic Python SDK via PydanticAI) | Chat completion with streaming, tool/function calling, structured JSON output, extended thinking, prompt caching |
| LLM Provider (secondary) | Agent inference — alternative models | OpenAI API (openai Python SDK via PydanticAI) | Chat completion with streaming, tool calling, structured output |
| LLM Provider (tertiary) | Agent inference — Copilot subscription models | GitHub Copilot SDK (codex-auth) | Chat completion with tool calling; prompted structured output (no native JSON mode) |
| Vector Store | Semantic search over knowledge entries | PostgreSQL pgvector extension | Cosine similarity search over embedding vectors; integrated with primary database |
| Container Runtime | Agent execution isolation | Docker / OrbStack | Container creation, command execution, environment cleanup; optional (local mode works without) |

### 2.2 External Integrations

| Integration | Direction | Protocol | Purpose |
|------------|-----------|----------|---------|
| Anthropic API | Outbound | HTTPS (REST) | LLM inference for agents (Claude models) |
| OpenAI API | Outbound | HTTPS (REST) | LLM inference for agents (GPT models) |
| GitHub Copilot | Outbound | HTTPS (OAuth + REST) | LLM inference for agents (Copilot models) |
| Perplexity API | Outbound | HTTPS (REST) | Web search (primary provider in search chain) |
| Tavily API | Outbound | HTTPS (REST) | Web search (secondary provider) |
| Brave Search API | Outbound | HTTPS (REST) | Web search (tertiary provider) |
| Jina Reader API | Outbound | HTTPS (REST) | URL content extraction (fallback after trafilatura/readability) |
| Telegram Bot API | Bidirectional | HTTPS (webhook + polling) | Notifications, chat, approval flows |
| PostgreSQL | Bidirectional | TCP (asyncpg) | All persistent data |
| Redis | Bidirectional | TCP (redis-py) | Event pub/sub, caching, idempotency, SSE tokens |
| Temporal Server | Bidirectional | gRPC | Workflow orchestration, schedule management |

### 2.3 Upstream and Downstream Dependencies

**Upstream (Mission Control consumes):**
- LLM providers (Anthropic, OpenAI, GitHub Copilot) for agent inference
- Web search providers (Perplexity, Tavily, Brave) for research tasks
- Content extraction services (Jina Reader) for URL fetching
- Telegram Bot API for channel integration
- PostgreSQL for persistence
- Redis for event transport and caching
- Temporal for durable workflow execution
- Docker/OrbStack for container-mode agent isolation

**Downstream (Mission Control serves):**
- Human users via CLI, TUI, React frontend, Telegram
- Programmatic consumers via REST API and SSE streams
- Internal AI agents via service layer, tool system, and event bus

---

## 3.0 Domain Model

### 3.1 Domain Primitives

> Extract from: data models, ORM definitions, Pydantic/dataclass schemas, type aliases, enums.
> For each entity, capture fields, types, constraints, and relationships. Strip framework-specific annotations and express as logical data types.

> **Full entity-level detail** (every column, type, constraint, relationship, and index for all 34 domain entities) is in the companion appendix:
>
> **[029a-domain-model-reference.md](029a-domain-model-reference.md)**
>
> That file contains the complete data dictionary for: Mission, MissionMember, MissionContext, ContextChange, ContextMilestone, MissionIntent, MissionLearning, MissionEvent, MissionArtifact, MissionPlan, PlanTaskState, MissionTeamMember, MissionDecision, MilestoneSummary, Session, SessionChannel, SessionMessage, SessionFollowup, ExecutionRecord, TaskExecution, TaskAttempt, ExecutionDecision, ExecutionEnvironmentModel, WorkflowDefinition, WorkflowRun, WorkflowSchedule, AgentCatalog, PromptVersion, User, ApiKey, LlmCall, ModelPricing, Embedding, HumanInputRequest.

### 3.2 Domain Hierarchy

> Extract from: foreign keys, composition patterns, parent-child relationships, orchestration flow.

```
Mission
├── MCD (Mission Context Document) — versioned, 1:1
├── Intent — versioned, current + history
├── Team (Roster) — mission_team_members → agent_catalog
├── Workflow Definitions — mission-scoped or global (mission_id=NULL)
│   └── Workflow Runs
│       └── Workflow Run Waves
├── Execution Records
│   ├── TaskPlan (DAG)
│   │   └── Tasks → Agent assignments
│   ├── Task Executions
│   │   └── Task Attempts (retries)
│   └── LLM Calls (cost tracking)
├── Sessions
│   └── Session Messages (conversation history)
├── Learnings — mission-scoped, promotable to platform
├── Artifacts — execution outputs
└── Workflow Schedules — cron-based
```

**Hierarchy Rules:**
- A Mission has exactly one MCD, created at mission creation with a seed structure
- A Mission has exactly one active MissionPlan at a time (unique constraint on mission_id)
- Execution Records are scoped to a Mission; deleting a Mission cascades through all related data (FK-ordered deletes + Temporal workflow termination + container cleanup)
- Sessions are shared across client surfaces — CLI, TUI, and frontend see the same conversation history via the same session_id
- Workflow Definitions can be global (mission_id=NULL for system templates) or mission-scoped
- Learnings are mission-scoped by default but can be promoted to platform-wide (platform_promoted=True)
- MissionMembers represent human membership (owner/maintainer/viewer); MissionTeamMembers represent agent membership (from agent catalog)
- Agent models are pinned per agent config — model changes require a version bump, not runtime override
- MissionIntent is versioned: each revision links to its predecessor via revised_from FK
- PlanTaskState has a unique constraint on (mission_plan_id, task_id) — each task appears once per plan
- ExecutionRecord can have a parent (parent_execution_record_id self-FK) for nested/child executions

### 3.3 Enumerations and Constants

| Name | Values | Used By | Description |
|------|--------|---------|-------------|
| MissionStatus | active, paused, archived | Mission.status | Mission lifecycle state |
| MissionTier | user, platform | Mission.tier | Access control tier |
| MissionMemberRole | owner, maintainer, viewer | MissionMember.role | Human membership role |
| MissionPlanStatus | active, paused, completed, archived | MissionPlan.status | Plan lifecycle state |
| PlanTaskStatus | proposed, approved, queued, running, completed, failed, skipped, cancelled | PlanTaskState.status | Individual task state in a plan DAG |
| SessionStatus | active, suspended, completed, expired, failed | Session.status | Session lifecycle (valid transitions enforced) |
| ExecutionRecordStatus | pending, running, completed, failed, cancelled, timed_out | ExecutionRecord.status | Execution lifecycle |
| TaskExecutionStatus | completed, failed, skipped | TaskExecution.status | Task outcome |
| TaskAttemptStatus | passed, failed | TaskAttempt.status | Individual attempt result |
| DecisionType | retry, fail, pass, re_plan, skip, escalate, post_run_supervisor | ExecutionDecision.decision_type | Dispatch decision classification |
| FailureTier | tier_1_structural, tier_2_quality, tier_3_integration, agent_error, timeout | TaskAttempt.failure_tier | Verification failure severity |
| WorkflowRunState | pending, running, awaiting_input, completed, failed, cancelled | WorkflowRun.status | Workflow run lifecycle |
| HumanInputRequestStatus | pending, completed, timed_out, cancelled | HumanInputRequest.status | Human input request state |
| ChangeType | add, replace, remove, prune, archive | ContextChange.change_type | MCD mutation operation |
| DecisionStatus | active, superseded, reversed | MissionDecision.status | Architectural decision state |
| LearningKind | failure_pattern, success_pattern, anti_pattern, retry_strategy, capability_gap | MissionLearning.kind | Learning classification (stored as String) |
| LearningStatus | active, superseded, retracted | MissionLearning.status | Learning lifecycle (stored as String) |
| IntentSource | user_chat, user_cli, director_inferred, system | MissionIntent.source | Intent origin (stored as String) |
| ArtifactType | research_report, vulnerability_report, quality_audit, architecture_review, report, code, data | MissionArtifact.artifact_type | Artifact classification (stored as String) |
| ArtifactContentType | text, markdown, json, code | MissionArtifact.content_type | Content format (stored as String) |
| EnvironmentStatus | creating, running, stopped, failed, destroyed | ExecutionEnvironment.status | Container/worktree state |
| SystemRole | viewer, user, admin, sysadmin | User.system_role | Platform RBAC (levels in security.yaml) |

---

## 4.0 Functional Requirements

### 4.1 Mission Lifecycle Management

> Extract from: mission creation endpoints, state transition logic, completion/failure handlers, cleanup routines.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-001 | The system shall support mission creation with name, description, owner, optional brief/budget/gate_mode/tier | Mission created with seed MCD, owner membership, and auto-assigned agents | services/mission.py |
| FR-002 | The system shall support mission deletion with cascading cleanup of all related data | All FK-ordered dependent data deleted; Temporal workflows terminated; containers cleaned | services/mission.py |

**State Transitions:**

| From State | To State | Trigger | Conditions | Side Effects |
|-----------|----------|---------|------------|-------------|
| active | paused | Manual pause (update_mission) | Mission is active | — |
| active | archived | archive_mission | Mission is active | MCD and history preserved |
| archived | active | unarchive_mission | Mission is archived | — |
| Any | deleted | delete_mission | User confirms | Cascading FK-ordered deletes, Temporal terminate, container cleanup |

### 4.2 Context Assembly and Memory Management

> Extract from: context_assembler.py, session_memory.py, context_curator.py, mission_context.py, knowledge.py, history_query.py, prompt_builder.py, embeddings.py, knowledge_helpers.py.
> This is the core mechanism that determines what every agent sees, remembers, and can process. Capture the full pipeline, not just individual components.

#### 4.2.1 Context Assembly Pipeline

> Extract from: ContextAssembler.build(), dispatch loop context injection, token budget config.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-003 | The system shall assemble a layered context packet for each agent before task execution | Context packet contains all required layers within budget | |
| FR-004 | Context assembly shall enforce a configurable token budget with a strict priority trim order | Layers are trimmed in defined order; PCD and task definition are never trimmed | |
| FR-005 | Context layer inclusion shall be driven by domain tags on the task | Coding tasks receive Code Map; non-coding tasks do not | |

**Context Layers (priority order — last trimmed first):**

| Layer | Name | Priority | Trim Behaviour | Source |
|-------|------|----------|---------------|--------|
| 0 | Mission Context Document (PCD/MCD) | Highest | Never trimmed | |
| 1a | Task definition | Highest | Never trimmed | |
| 1b | Resolved upstream inputs | High | Summarized if over budget | |
| 3 | Code Map (codebase structure) | Medium | Token-capped; loaded only for coding tasks | |
| 2.5 | Learnings (reusable strategy) | Medium | Skipped if budget < threshold | |
| 2 | Execution history (failures, recent runs) | Lowest | Reduced or removed first | |

**Configurable Parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| context_token_budget | | Total token budget for assembled context |
| history_reserve_tokens | | Tokens reserved for history before Code Map fills budget |
| learnings_reserve_tokens | | Tokens reserved for learnings in context |
| learnings_max_entries | | Max learning entries injected into context |
| code_map_max_tokens | | Max tokens for Code Map rendering |

#### 4.2.2 Mission Context Document (MCD) Lifecycle

> Extract from: MissionContextManager, MissionContext model, ContextChange model, ContextMilestone model, context_curator.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-006 | Each mission shall have a versioned, persistent JSON document (MCD) that accumulates knowledge across runs | MCD created on mission creation with seed structure | |
| FR-007 | MCD mutations shall use path-based operations (add, replace, remove) with dot notation | All three operations work on nested paths | |
| FR-008 | Restricted paths (version, last_updated, last_updated_by) shall be immutable by agents | Agent attempts to modify restricted paths are rejected | |
| FR-009 | Agents shall not be able to remove guardrail entries | Guardrail removal by agents returns an error | |
| FR-010 | MCD size shall be enforced against configurable max and target byte limits | Updates that exceed max size are rejected with an error | |
| FR-011 | MCD shall use optimistic concurrency on the version field | Concurrent updates are detected and rejected | |
| FR-012 | All MCD changes shall be recorded in an audit trail with agent_id, task_id, path, old/new value, and reason | Audit trail is queryable per mission | |
| FR-013 | Named milestone snapshots of MCD state shall be creatable for point-in-time reference | Milestones store a deep copy of the MCD at a specific version | |

**MCD Seed Structure:**

| Section | Purpose |
|---------|---------|
| identity | Mission name, purpose, tech stack, repo structure |
| architecture | Components, data flow, conventions |
| decisions | Accumulated architectural/design decisions |
| current_state | Active workstreams, milestones, known issues, priorities |
| guardrails | Constraints that agents must respect |

**MCD Caching:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| cache_ttl_seconds | | In-memory cache TTL before DB re-read |

#### 4.2.3 Context Curation

> Extract from: ContextCurator, domain_tags.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-014 | Context updates from agent task results shall be validated before MCD application | Invalid domain tags are dropped; invalid operations logged | |
| FR-015 | Domain tags in context updates shall be validated against a controlled vocabulary | Invalid tags are silently dropped (never block the parent write) | |

#### 4.2.4 Session Memory Compression

> Extract from: SessionMemoryService, Session model (rolling_summary fields), system.summarization.agent.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-016 | Long sessions shall be compressed via anchored rolling summary when message count since last anchor exceeds a threshold | Compression fires automatically; rolling summary updated | |
| FR-017 | Rolling summaries shall be produced by a summarization agent, not simple truncation | Summary preserves semantic content of the compressed span | |
| FR-018 | Sessions shall track rolling_summary, rolling_summary_anchor, and rolling_summary_version | Fields updated atomically after compression | |

**Configurable Parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| compression_threshold_messages | | Messages since last anchor before compression fires |
| summary_max_tokens | | Max tokens for the rolling summary |

#### 4.2.5 Conversation History Persistence

> Extract from: history.py (ModelMessage conversion), session_messages table, SessionMessageCreate schema.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-019 | Session messages shall be persisted with full round-trip fidelity (user, assistant, system, tool_call, tool_result) | All message roles are stored and recoverable | |
| FR-020 | Persisted messages shall be convertible to the agent framework's message format for context injection | Loaded history produces valid agent message_history input | |
| FR-021 | Cost metadata (model, input_tokens, output_tokens, cost_usd) shall be attached to the last assistant message | Cost is attributable per interaction | |

#### 4.2.6 Prompt Layering

> Extract from: prompt_builder.py, config/prompts/ directory structure.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-022 | Agent system prompts shall be assembled from five ordered layers | All five layers compose into a single prompt string | |

**Prompt Layers (assembly order):**

| Layer | Source | Scope | Description |
|-------|--------|-------|-------------|
| 1 | Organization prompts | Global | Loaded from disk, shared across all agents |
| 2 | Category prompt | Per agent category | Loaded from disk by category name |
| 3 | Agent system prompt | Per agent | Loaded from disk; supports variant selection |
| 4 | Mission brief | Per mission | From DB (missions.brief) |
| 5 | User prompt | Per agent per mission | From DB (mission_team_members.user_prompt) |

#### 4.2.7 Knowledge Platform and Search

> Extract from: KnowledgeService, EmbeddingRepository, MissionLearningRepository, MissionIntentRepository, MissionDecisionRepository, MilestoneSummaryRepository.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-023 | The system shall maintain five distinct knowledge surfaces per mission | All five surfaces are queryable via a unified API | |
| FR-024 | Knowledge search shall support three modes: structured, semantic, and hybrid | Each mode produces ranked results | |
| FR-025 | Semantic search shall use vector embeddings with a configurable embedding model | Embeddings are generated and stored for learnings, decisions, milestones | |
| FR-026 | Hybrid search shall filter structurally then rerank by vector distance | Results combine tag precision with semantic relevance | |
| FR-027 | Learnings shall support platform promotion (mission-scoped → platform-wide) | Platform-promoted learnings appear in cross-mission searches | |

**Knowledge Surfaces:**

| Surface | Entity | Scope | Description |
|---------|--------|-------|-------------|
| MCD | MissionContext | Per mission | Versioned project context document |
| Intent | MissionIntent | Per mission | Current mission objective and scope |
| Learnings | MissionLearning | Per mission / platform | Extracted patterns with confidence scores |
| Decisions | MissionDecision | Per mission | Architectural and design decisions |
| Milestones | MilestoneSummary | Per mission | Compressed execution summaries |

**Search Modes:**

| Mode | Mechanism | Cost | Use Case |
|------|-----------|------|----------|
| structured | Tag/kind/status filter | Zero embedding calls | When filters are precise |
| semantic | Embed query → nearest neighbours | One embedding call | When query is natural language |
| hybrid | Structured filter → vector rerank | One embedding call | Default; combines precision and recall |

#### 4.2.8 Embedding Lifecycle

> Extract from: EmbeddingRepository, knowledge_workflow.py (EmbeddingBackfillWorkflow), knowledge_helpers.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-028 | Embeddings shall be generated for learnings, decisions, and milestones | All three entity types have embeddings after backfill | |
| FR-029 | Embedding backfill shall run on a scheduled cron job | New/updated entries without embeddings are backfilled automatically | |
| FR-030 | Embeddings shall be versioned to support model upgrades | Version field enables re-embedding when the model changes | |

#### 4.2.9 History Exclusion Rules

> Extract from: HistoryQueryService (include_summarized parameter), summarization pipeline.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-031 | Summarized executions shall be excluded from context assembly history by default | Agents do not receive duplicate context from pre-summarized runs | |
| FR-032 | History queries shall support an explicit opt-in to include summarized data | Admin/diagnostic queries can access full history | |

#### 4.2.10 Context Update Flow

> Extract from: dispatch loop → ContextCurator → MissionContextManager → ContextChange audit → cache invalidation.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-033 | Agent task results containing context_updates shall flow through validation → mutation → audit → cache invalidation | End-to-end flow produces a new MCD version with audit entries | |
| FR-034 | Context update errors shall be non-fatal — logged and skipped, never failing the parent task | Task completes even if some context updates are invalid | |

### 4.3 Agent Orchestration

> Extract from: agent lifecycle management, tool registration, LLM interaction patterns, structured output handling, inter-agent communication.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-035 | The system shall orchestrate agent execution through a dispatch loop that routes tasks to agents, tracks cost, streams events, and handles retries | Tasks are dispatched to the correct agent; cost is recorded per invocation; events are emitted; failed tasks retry up to budget | `agents/mission_control/` |

**Orchestration Patterns:**

> Extract from: orchestration/coordination modules. For each pattern found, describe the behaviour, not the implementation.

| Pattern | Description | When Used |
|---------|-------------|-----------|
| Sequential | | |
| Parallel (fan-out/fan-in) | | |
| Conditional routing | | |
| Human-in-the-loop | | |
| [Others found in codebase] | | |

**Agent-to-Agent Communication:**

| Mechanism | Description | Constraints |
|-----------|-------------|-------------|
| | | |

### 4.4 Session Management

> Extract from: session creation, message routing, transport abstraction, client surface adapters.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-036 | The system shall manage session lifecycle (create, suspend, resume, complete, fail, expire) with valid state transitions, cost tracking, channel binding, and message persistence | State transitions enforced by VALID_TRANSITIONS; budget warnings at threshold; multi-channel binding via session_channels | `services/session.py` |

**Transport Abstractions:**

| Transport | Protocol | Session Semantics | Message Format |
|-----------|----------|-------------------|---------------|
| REST API | HTTP/JSON | Session created via POST; referenced by session_id in subsequent calls | JSON (Pydantic schemas) |
| SSE Streams | Server-Sent Events over HTTP | Subscribed by session_id or mission_id; Redis pub/sub delivers events | JSON event payloads |
| CLI (mc.py) | HTTP client to REST API | Session auto-created or resumed; token stored locally | JSON over HTTP |
| TUI (tui.py) | In-process service calls | Direct service layer access; session shared with other surfaces | Service method calls |
| Telegram | Webhook + Bot API | Channel binding maps Telegram chat_id to session_id | Telegram message format → internal session messages |

### 4.5 Gate System (Human-in-the-Loop Approval)

> Extract from: gate.py, gate_models.py, gate_prompt.py, gate_post_run.py, approval.py, escalation.py, gate_helpers (clients), gate_modal (TUI), gate.yaml config.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-037 | The system shall support configurable human-in-the-loop approval gates at plan, task, and post-run stages | Gates fire at the configured stages | |
| FR-038 | Gate modes shall include: off, interactive, ai_assisted, autonomous | Each mode produces the correct approval flow | |
| FR-039 | In AI-assisted mode, an LLM shall evaluate the gate decision and present a recommendation to the human | Recommendation includes rationale and suggested action | |
| FR-040 | Gate decisions shall be surfaced across all client surfaces (CLI, TUI, frontend) | The same gate prompt renders correctly in each client | |
| FR-041 | Gate mode shall be configurable per mission (default) and overridable per run | Run-level override takes precedence over mission default | |

**Gate Modes:**

| Mode | Behaviour | Use Case |
|------|-----------|----------|
| off | No gates — fully autonomous execution | Trusted, low-risk workflows |
| interactive | Human must approve each gate | Step-by-step review |
| ai_assisted | LLM evaluates and recommends; human confirms | Balanced autonomy |
| autonomous | LLM evaluates and auto-approves if confidence is sufficient | High-trust, high-volume |

**Gate Stages:**

| Stage | When | What is Reviewed |
|-------|------|-----------------|
| Plan review | After plan generation, before execution | Task DAG, agent assignments, estimated cost |
| Task review | Before each task dispatches | Task inputs, agent selection |
| Post-run review | After execution completes | Outcomes, artifacts, cost actuals |

### 4.6 Plan Management and DAG Execution

> Extract from: mission_plan.py, mission_plan_orchestrator.py, plan_validator.py, dispatch/loop.py, dispatch/dag.py, dispatch/step_executor.py, dispatch/retry_handler.py, dispatch/convergence.py, mission_plan_crash_recovery.py, task_plan schema.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-042 | The system shall decompose objectives into a directed acyclic graph (DAG) of tasks via a planning agent | Planning agent produces a valid TaskPlan with tasks, dependencies, and agent assignments | |
| FR-043 | Tasks in a DAG shall execute respecting dependency order — independent tasks may run in parallel | Dependent tasks wait; independent tasks dispatch concurrently | |
| FR-044 | Plans shall be validatable against the available agent roster before execution begins | Invalid agent references, circular dependencies, and contract mismatches are caught pre-execution | |
| FR-045 | Plans shall support refinement — modifying an existing plan while preserving task IDs for unchanged tasks | Refined plans carry forward approval state and history for reused task IDs | |
| FR-046 | Failed tasks shall support retry with configurable retry budgets | Retries respect per-task retry_budget; exhausted retries escalate | |
| FR-047 | Orphaned task states from crashes shall be reconciled on startup | Crash recovery detects stale RUNNING/DISPATCHING states and transitions them | |

**Task States:**

| State | Description |
|-------|-------------|
| proposed | Task created by Planning Agent; not yet approved |
| approved | Human or autonomous approval granted; ready for dispatch |
| queued | Dispatched to execution queue; dependencies satisfied |
| running | Agent currently executing this task |
| completed | Task completed successfully; output stored |
| failed | Task failed after exhausting retry budget |
| skipped | Task bypassed (dependency failed or plan revised) |
| cancelled | Task cancelled by user or system |

**Plan Validation Checks:**

| Check | Description |
|-------|-------------|
| Agent existence | Referenced agent_name exists in the roster |
| Dependency validity | depends_on task_ids exist in the plan; no circular dependencies |
| Interface compatibility | Task inputs satisfy the assigned agent's declared input contract |
| Deterministic check names | tier_2.deterministic_checks reference registered check functions (validate_json_schema, validate_field_exists, validate_field_type, validate_field_range) |
| Budget feasibility | Estimated cost does not exceed remaining budget |
| DAG acyclicity | Task dependency graph is a valid DAG |

### 4.7 Cost Tracking and Token Economics

> Extract from: cost.py, cost_alerts.py, token_economics.py, model_pricing.py, llm_call model, cost.yaml config.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-048 | Every LLM call shall be recorded with model, input/output tokens, and computed cost | LLM call records are queryable per execution, agent, and mission | |
| FR-049 | Missions shall have configurable budget ceilings in USD | Execution halts when budget is exhausted | |
| FR-050 | Cost tracking shall be attributable at task, execution, and mission granularity | Cost rollups are accurate at each level | |
| FR-051 | Model pricing shall be configurable and updatable without code changes | Pricing loaded from config/DB, not hardcoded | |
| FR-052 | Cost alerts shall fire when spending exceeds configurable thresholds | Alerts surface through the event system | |

### 4.8 Verification and Output Validation

> Extract from: verification.py, verification_tiers.py, verification_models.py, system/verification/agent.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-053 | Agent task outputs shall be verified through a multi-tier verification system | Verification tier is selected based on task risk/importance | |
| FR-054 | Verification failures shall produce structured feedback for retry | Failed verification includes actionable feedback for the executing agent | |

**Verification Tiers:**

| Tier | Description | When Used |
|------|-------------|-----------|
| Tier 1 — Structural | Validates output is a non-empty dict; checks required fields and interface contract field presence. Deterministic, no LLM. | All tasks (unless schema_validation disabled) |
| Tier 2 — Deterministic | Runs registered named checks (validate_json_schema, validate_field_exists, validate_field_type, validate_field_range) with configurable parameters. No LLM. | Tasks that declare tier_2.deterministic_checks in the TaskPlan |
| Tier 3 — LLM-based | Delegates to the verification agent for semantic quality assessment. Returns structured Tier3Result with pass/fail + feedback. | High-risk or high-importance tasks as declared in the TaskPlan |

### 4.9 Research Pipeline

> Extract from: research/strategist, research/searcher, research/analyst, research/synthesizer, research/verifier agents; dispatch/research_loop.py; research schema.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-055 | The system shall support a multi-phase research pipeline with strategy, search, analysis, synthesis, and verification stages | Research tasks flow through all five stages | |
| FR-056 | Research results shall be synthesized into structured reports with citations | Final output contains findings traceable to sources | |
| FR-057 | Research phases shall support iterative deepening based on verification feedback | Verifier can trigger additional search/analysis cycles | |

**Research Phases:**

| Phase | Agent | Purpose |
|-------|-------|---------|
| Strategy | research.strategist | Define search strategy and decompose research questions |
| Search | research.searcher | Execute web/codebase searches |
| Analysis | research.analyst | Analyse and extract findings from raw results |
| Synthesis | research.synthesizer | Compose findings into a coherent report |
| Verification | research.verifier | Validate claims, check citations, identify gaps |

### 4.10 Execution Environments and Agent Isolation

> Extract from: execution_environment.py, container_backend.py, environments.yaml config.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-058 | Agents shall optionally execute in isolated container environments | Container-mode tasks run in sandboxed Docker containers | |
| FR-059 | Execution environments shall be configurable per mission (local vs container mode) | Mode selection is per-mission, not hardcoded | |
| FR-060 | Idle execution environments shall be cleaned up on a scheduled basis | Cleanup workflow removes expired/idle containers | |

### 4.11 Agent Catalog and Roster Management

> Extract from: agent_catalog model/repo/service, catalog_seeder.py, mission_team model/repo/service, prompt_version model, roster.py, config/agents/ YAML files.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-061 | The system shall maintain a seedable agent catalog with versioned definitions | Catalog is populated from YAML config; re-seedable with --force | |
| FR-062 | Agents shall be assignable to mission teams (roster) with per-agent customisation | Mission teams have agent membership with optional user_prompt overrides | |
| FR-063 | Agent system prompts shall be versioned with change tracking | Prompt updates create new versions; old versions remain queryable | |
| FR-064 | Agents shall be individually enable/disable toggleable | Disabled agents are excluded from routing and dispatch | |

### 4.12 Multi-Channel Gateway

> Extract from: gateway/adapters/, gateway/registry.py, gateway/security/, telegram adapter, features config.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-065 | The system shall support multiple client channel adapters via a gateway registry | New channels can be added by implementing the adapter interface | |
| FR-066 | Channels shall be individually enable/disable via feature flags | Disabled channels are not mounted at startup | |
| FR-067 | Each channel adapter shall enforce its own security constraints (rate limiting, allowlists) | Per-channel rate limits and allowlists are configurable | |
| FR-068 | Security startup checks shall validate channel configuration before accepting traffic | Missing secrets or invalid config causes startup failure, not silent degradation | |

### 4.13 Workflow Management

> Extract from: workflow_definition model/service, workflow_run model/service, workflow_run_waves.py, workflow_schedule model/service, workflow_seeder.py, workflow_validator.py, config/workflow_templates/ YAML.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-069 | The system shall support reusable workflow definitions seeded from YAML templates | System workflows exist globally (mission_id=NULL); user workflows are mission-scoped | |
| FR-070 | Workflows shall execute as ordered waves of tasks with dependency tracking | Wave execution respects inter-task dependencies | |
| FR-071 | Workflows shall be schedulable via cron expressions with cooldown and iteration limits | Scheduled workflows execute on cron; cooldown prevents overlap | |
| FR-072 | Workflow definitions shall be validatable before execution | Invalid definitions are rejected with specific error messages | |

### 4.14 Tool System

> Extract from: modules/backend/tools/ (all files), tools/registry.py, agents/adapters/ (all files), agents/deps/base.py (FileScope), agent config YAML (scope.read, scope.write, tools list).

#### 4.14.1 Tool Registry

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-073 | The system shall auto-discover tool modules from a tools package and register them on agents at creation time | Tools discovered via TOOL_DEF export; register() function called on agent | |
| FR-074 | Each tool shall declare a TOOL_DEF with name, description, category, and required deps | Missing TOOL_DEF silently skips the module | |
| FR-075 | Tool registration on agents shall be selective — agents only receive tools listed in their config | An agent configured with ["read_file", "list_files"] cannot call search_web | |

#### 4.14.2 Filesystem Access Control (FileScope)

> Extract from: agents/deps/base.py (FileScope class), agent YAML configs (scope.read, scope.write).

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-076 | Each agent shall have a configurable FileScope defining allowed read and write paths | Scope loaded from agent YAML config at invocation time | |
| FR-077 | All filesystem tool operations (read_file, list_files, apply_fix) shall enforce FileScope checks before any I/O | PermissionError raised for out-of-scope access | |
| FR-078 | FileScope shall support path prefixes, wildcard ("*"), and extension patterns ("*.py") | Matching rules are deterministic and testable | |
| FR-079 | Write scope shall be strictly narrower than or equal to read scope | Agents cannot write to paths they cannot read | |

**FileScope Enforcement Points:**

| Tool | Check | Behaviour on Violation |
|------|-------|----------------------|
| read_file | scope.check_read(path) | PermissionError raised |
| list_files | scope.is_readable(path) per file | Out-of-scope files silently excluded |
| apply_fix | scope.check_write(path) | PermissionError raised |
| run_command | environment_handle isolation | Runs in sandboxed container/worktree when configured |

#### 4.14.3 Tool Implementations

| Tool | Category | Purpose | Security Boundary |
|------|----------|---------|------------------|
| read_file | filesystem | Read file contents with line numbers | FileScope read check |
| list_files | filesystem | List Python files within scope | FileScope read filter + exclusion patterns |
| apply_fix | code | Replace exact text in a file (must appear exactly once) | FileScope write check |
| run_command | execution | Execute shell command in execution environment | Container/worktree isolation when configured; timeout protection |
| run_tests | execution | Run test suites | Inherits execution environment constraints |
| search_web | search | Web search via provider chain (perplexity > tavily > brave) | No filesystem access; HTTP client injected |
| fetch_url | search | Fetch URL and extract readable text (trafilatura > readability > Jina) | No filesystem access; max_length cap; timeout protection |
| search_codebase | search | Search code within project | FileScope read scope |
| query_knowledge | knowledge | Query the knowledge platform | Mission-scoped |

#### 4.14.4 Agent Adapters (Tool Composition)

> Extract from: modules/backend/agents/adapters/ — these compose tools into thematic bundles registered onto agents.

| Adapter | Tools Composed | Purpose |
|---------|---------------|---------|
| filesystem | read_file, list_files | Basic filesystem read access |
| code | apply_fix, run_tests | Code modification and testing |
| research | fetch_url, search_codebase, search_web | Information gathering |
| codemap | Code Map loader, PQI scorer, Bandit, Radon | Codebase structural analysis |
| compliance | ComplianceScannerService | Rule-based code auditing |
| security_scanning | SARIF tools, tool_registry, tool_runner | Security tool orchestration |
| followup | SessionFollowupService | Wake-on-event scheduling |
| intent | MissionIntentService | Mission intent read/write |
| plan_introspection | MissionPlanService | Plan inspection from within agents |
| supervisor_introspection | Plan introspection + delegation | Supervisor-level plan access |
| workflow_design | Catalog, team, workflow definition services | Workflow architect tooling |

### 4.15 Agent Authentication, Invocation Tiers, and Constraints

> Extract from: agents/config_schema.py (InvocationSchema, AgentConfigSchema), agents/deps/base.py (BaseAgentDeps, HorizontalAgentDeps), middleware.py (check_guardrails), agent YAML configs.

#### 4.15.1 Agent Invocation Tiers

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-080 | Each agent shall have a configurable invocation tier controlling who can invoke it | Tier enforced at dispatch time | |
| FR-081 | Agents with tier "platform" shall require admin role and can only run within platform-tier missions | Non-admin users cannot invoke platform agents | |
| FR-082 | Agents with tier "system" shall be invocable only by dispatch infrastructure, not by users | System agents are excluded from user-facing routing | |

**Invocation Tiers:**

| Tier | Who Can Invoke | Mission Tier Required | Example Agents |
|------|---------------|----------------------|---------------|
| user | Any authenticated user | Any | software.quality, research.* |
| platform | Admin role required | platform | platform.health, platform.director |
| system | Dispatch infrastructure only | Any | system.summarization, system.verification |

#### 4.15.2 Agent Execution Constraints

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-083 | Each agent shall have a configurable per-invocation cost ceiling (max_budget_usd) | Execution halts when agent cost exceeds ceiling | |
| FR-084 | Each agent shall have a configurable max input length | Inputs exceeding the limit are rejected before LLM call | |
| FR-085 | Each agent shall have configurable max_tokens and max_requests limits | Limits enforced per invocation | |
| FR-086 | Agent models shall be pinned and non-overridable at runtime | Model changes require a new agent version | |
| FR-087 | Each agent shall declare an execution engine (pydantic_ai, claude_code, codex, copilot) | Engine determines the runtime execution path | |
| FR-088 | Agent timeout shall be enforced per invocation | Timed-out agents are killed and reported as failures | |

#### 4.15.3 Input Guardrails

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-089 | All user input shall be checked against configurable injection patterns before any LLM call | Matching patterns raise ValueError; input is blocked | |
| FR-090 | Input length shall be validated against both mission-control-level and per-agent maximums | Shorter of the two limits applies | |

#### 4.15.4 Agent Interface Contracts

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-091 | Each agent shall declare a typed input/output interface contract | Contract defines field names and type names | |
| FR-092 | Plan validation shall check that task inputs match the assigned agent's input contract | Mismatched inputs caught before execution | |

#### 4.15.5 Horizontal Agent Delegation

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-093 | Supervisory agents shall have an explicit set of allowed delegation targets | HorizontalAgentDeps.allowed_agents constrains which agents can be delegated to | |
| FR-094 | Delegation depth shall be configurable and enforced | max_delegation_depth prevents unbounded delegation chains | |

### 4.16 Session Followup System (Autonomy Loop)

> Extract from: session_followup.py, session_followup model, SessionFollowupFiredEvent.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-095 | Agents shall be able to schedule wake-on-event followups that fire when a specified event occurs | Followup fires and agent receives rendered prompt | |
| FR-096 | Followups shall match on event type, with optional literal-match filter on event payload fields | Filter matching is deterministic and portable (no eval()) | |
| FR-097 | Followup prompts shall be template-rendered against the triggering event's payload | Missing template keys degrade to empty string, not crash | |
| FR-098 | Active followups per session shall be capped at a configurable maximum | Cap prevents runaway agents from registering unlimited followups | |
| FR-099 | Followups shall have a configurable expiry TTL | Expired followups are not fired | |
| FR-100 | Followup firing failures shall be recorded on the row, not bubbled | One failing followup does not prevent sibling followups from firing | |
| FR-101 | Followup firing shall emit audit events (SessionFollowupFiredEvent) | Success and failure events are published for observability | |

**Configurable Parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| max_active_followups_per_session | 5 | Per-session cap |
| default_expires_in_seconds | 86400 (24h) | TTL for followups without explicit expiry |

### 4.17 Notification System

> Extract from: run_notifier.py, notifications.yaml config, telegram/services/notifications.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-102 | The system shall dispatch lifecycle notifications for run completion, failure, budget warnings, and schedule errors | Notifications fire for configured event types | |
| FR-103 | Notification channels shall be configurable (currently: Telegram, SSE) | Channel list is config-driven, not hardcoded | |
| FR-104 | Budget warnings shall fire when spending exceeds a configurable threshold percentage | Threshold is configurable per deployment | |
| FR-105 | Each notification type shall be individually enable/disable | Selective notification opt-in | |

### 4.18 Mission Event Timeline (Persisted Audit Log)

> Extract from: mission_event model, SessionEventBus persist set, /narrative endpoint.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-106 | Narrative-relevant events shall be persisted as an append-only mission timeline | Events are durable and queryable after Redis pub/sub expires | |
| FR-107 | Only events in a configurable persist set shall be written to the timeline | Not all ephemeral events are persisted | |
| FR-108 | The timeline shall be queryable by mission, event type, and time range | Historical timeline supports filtering | |

### 4.19 Branch Management and Git Integration

> Extract from: branch_manager.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-109 | Agent-generated code changes shall use a standardised branch naming convention | Branches follow agent/{mission-id-short}/{task-slug} format | |
| FR-110 | Commit messages shall include agent attribution and task/mission metadata | Commits are traceable to the agent and task that produced them | |
| FR-111 | All git operations shall be async (non-blocking) | Git commands use asyncio subprocess, not blocking calls | |

### 4.20 Summarization Pipeline

> Extract from: summarization.py, system/summarization/agent.py, system/synthesis/agent.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-112 | Execution history shall be periodically compressed into milestone summaries | Compression reduces storage and prevents unbounded context growth | |
| FR-113 | Summarized execution records shall be marked and excluded from future context assembly by default | Agents do not receive duplicate context from compressed history | |
| FR-114 | Summarization shall be performed by a dedicated agent, not simple truncation | Summaries preserve semantic content | |

### 4.21 Condition Evaluator (Workflow Branching)

> Extract from: condition_evaluator.py.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-115 | Workflow condition nodes shall be evaluated by a safe expression evaluator with no eval() or arbitrary code execution | Conditions use a custom tokenizer/parser/evaluator | |
| FR-116 | Conditions shall support variable references (@node.step_id.field, @context.key), comparison operators, compound logic (AND/OR/NOT), and unary checks (is_empty, is_not_empty) | All documented operators produce correct results | |

### 4.22 Compliance Scanner

> Extract from: compliance.py, compliance adapter.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-117 | The system shall include a deterministic, rule-based compliance scanner for Python codebases (no LLM dependency) | Scanner produces findings without any model calls | |
| FR-118 | Compliance rules shall be configurable with per-rule severity and enable/disable | Rules loaded from config; disabled rules are skipped | |
| FR-119 | Scan exclusion paths and patterns shall be configurable | Excluded paths are not scanned | |

### 4.23 API Surface

> Extract from: route definitions, request/response models, middleware, SSE/streaming handlers.

> **Full endpoint-level detail** (every REST endpoint with method, path, schemas, auth, and every CLI command with options) is in the companion appendix:
>
> **[029b-api-surface-reference.md](029b-api-surface-reference.md)**
>
> Summary: 120+ REST endpoints across 21 router modules; 3 health endpoints at root; SSE streaming via 3 `/streams/` endpoints. Auth patterns: CurrentUser (JWT/API key), AdminRole (admin+), SseUser (SSE token/JWT), Public (no auth dependency in code).

#### 4.23.1 REST Endpoints

| Subsystem | Prefix | Endpoint Count | Auth Pattern |
|-----------|--------|---------------|-------------|
| Health | `/health` | 3 | Public |
| Auth | `/api/v1/auth` | 10 | Mixed (Public login/refresh; CurrentUser for rest) |
| Agents | `/api/v1/agents` | 3 | Public |
| Catalog | `/api/v1/catalog/agents` | 2 | Public |
| Sessions | `/api/v1/sessions` | 12 | Public |
| Executions | `/api/v1/executions` | 8 | Public |
| Missions (CRUD) | `/api/v1/missions` | 7 | CurrentUser |
| Missions — Team | `/api/v1/missions/{id}/team` | 11 | Public |
| Missions — Plan | `/api/v1/missions/{id}/plan` | 9 | CurrentUser |
| Missions — Context | `/api/v1/missions/{id}/context` | 3 | Public |
| Missions — Artifacts | `/api/v1/missions/{id}/artifacts` | 1 | CurrentUser |
| Missions — Costs | `/api/v1/missions/{id}/costs` | 1 | CurrentUser |
| Missions — Workflows | `/api/v1/missions/{id}/workflows` | 17 | Mixed (mostly Public; delete requires admin) |
| Missions — Knowledge | `/api/v1/missions/{id}/...` | 8 | CurrentUser |
| Platform Knowledge | `/api/v1/knowledge` | 6 | AdminRole |
| Workflows (legacy) | `/api/v1/workflows` | 5 | Public |
| Workshop | `/api/v1/workshop` | 3 | Public |
| Streams (SSE) | `/api/v1/streams` | 3 | SseUser |
| Client Errors | `/api/v1/client-errors` | 1 | Public |
| Admin | `/api/v1/admin` | 21 | AdminRole |
| Admin Users | `/api/v1/admin/users` | 10 | AdminRole |

#### 4.23.2 Real-Time Event Channels

> Extract from: streams.py (SSE endpoints), event bus (Redis pub/sub), SessionEventBus. Note: the current implementation uses Server-Sent Events (SSE), not WebSocket. Capture the requirement (real-time server-to-client event streaming) protocol-neutrally.

| Channel/Path | Purpose | Event Types | Direction | Transport |
|-------------|---------|------------|-----------|-----------|
| `/api/v1/streams/runs/{session_id}` | Run event stream for a session | agent.*, plan.*, session.cost.* | Server → Client | SSE (SseUser auth) |
| `/api/v1/streams/missions/{mission_id}` | Mission console stream | All mission-scoped events | Server → Client | SSE (SseUser auth) |
| `/api/v1/streams/notifications` | User notification stream | Notification events (completion, failure, budget warning) | Server → Client | SSE (SseUser auth) |
| `mission_events:{mission_id}` | Internal event bus channel | All event types (except exclude list) | Service → Bus → Subscribers | Redis pub/sub |
| `notify:mission:{mission_id}` | Notification fan-out channel | Notification payloads | RunNotifierService → Frontend | Redis pub/sub |

**Event Bus Requirements:**

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-120 | The system shall provide real-time server-to-client event streaming for mission activity | Events stream to CLI, TUI, and frontend simultaneously | |
| FR-121 | Event channels shall be scoped per session/mission | Clients only receive events for their active context | |
| FR-122 | The event bus shall support multiple concurrent subscribers | CLI, TUI, and frontend can all subscribe to the same mission's events | |

#### 4.23.3 CLI Commands

> Extract from: CLI entry points (mc.py, admin.py), argument parsers, subcommands.

> Full CLI command trees (mc.py: 40+ commands, admin.py: 70+ commands) with all options, types, and defaults are documented in the companion appendix:
>
> **[029b-api-surface-reference.md](029b-api-surface-reference.md)** — CLI Commands section

### 4.24 Durable Workflow Execution (Infrastructure)

> Extract from: Temporal workflow definitions, activity implementations, retry policies, compensation logic, signal/query handlers, temporal/ module.
> Note: This section covers the infrastructure-level durable execution engine. Domain-level workflow management is in Section 4.13. Plan DAG execution is in Section 4.6.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-123 | The system shall execute mission runs, chat turns, plan refinements, tool forks, scheduled checks, maintenance jobs, knowledge pipelines, and human input pauses as durable Temporal workflows with configurable timeouts, retry policies, and signal/query support | Each workflow type listed in the Workflow Types table completes or fails deterministically; crash recovery resumes from last checkpoint | `temporal/*.py` |

**Workflow Types:**

| Workflow | Trigger | Duration | Key Activities | Compensation / Failure |
|---------|---------|----------|---------------|----------------------|
| MissionRunWorkflow | MissionPlanOrchestrator or ScheduledMissionCheckWorkflow | Minutes to hours (loops until terminal) | execute_mission → persist_mission_outcome → evaluate_post_run → route (wait/advance/refine/propose/escalate) | BudgetExceededError non-retryable; supervisor iteration cap; escalation to Director |
| ChatTurnWorkflow | Supervisor start_director_turn or tool_fork notify_director | Seconds to minutes (single turn) | persist_user_message → execute_chat_turn | BudgetExceededError non-retryable; signals parent refinement_complete |
| RunObjectiveWorkflow | API POST /plan/run-objective | Minutes | execute_run_objective (refine → approve → dispatch → starts MissionRunWorkflow) | ConflictError/ValidationError non-retryable; max 2 attempts |
| PlanRefineWorkflow | API POST /plan/refine | Seconds to minutes | execute_plan_refine (Planning Agent refines plan; SSE notifies frontend) | Max 2 attempts |
| DesignWorkflowWorkflow | Director tool fork (design_workflow) | Minutes | update_execution_record_status → execute_design_workflow → notify_director | Marks execution failed on error; max 1 retry |
| RunWorkflowWorkflow | Director tool fork (run_workflow) | Minutes to hours | update_execution_record_status → execute_run_workflow → notify_director | Marks execution failed on error; followup event types for matching |
| HumanInputWorkflow | WorkflowRunWaves (human_input step type) | Configurable timeout (default hours) | notify_human_input_needed → wait_condition (signal or timeout) → mark_human_input_resolved | Timeout → default_response or timed_out status; max 3 notify retries |
| AgentMissionWorkflow | API POST /executions/{id}/execute (legacy) | Minutes | execute_mission → persist_mission_outcome → optional approval signal wait → single retry | BudgetExceededError non-retryable; approval signal with escalation |
| ScheduledMissionCheckWorkflow | Temporal schedule (configurable interval) | Seconds | find_ready_plans → start child MissionRunWorkflow per plan (ParentClosePolicy.ABANDON) | Max 2 attempts on find_ready_plans |
| DailyCleanupWorkflow | Temporal cron 0 2 * * * | Seconds | run_daily_cleanup (purge expired sessions, stale data) | Max 1 attempt |
| HourlyHealthCheckWorkflow | Temporal cron 0 * * * * | Seconds | run_hourly_health_check (PostgreSQL + Redis checks) | Max 1 attempt |
| WeeklyReportWorkflow | Temporal cron 0 6 * * 0 | Seconds to minutes | run_weekly_report_generation | Max 2 attempts |
| MetricsAggregationWorkflow | Temporal cron */15 * * * * | Seconds | run_metrics_aggregation | Max 1 attempt |
| CleanupEnvironmentsWorkflow | Temporal cron 0 * * * * | Seconds | run_cleanup_stale_environments | Max 1 attempt |
| EvaluateWorkflowSchedulesWorkflow | Temporal cron * * * * * | Seconds | run_evaluate_workflow_schedules (evaluate user cron schedules, trigger due runs) | Max 1 attempt |
| KnowledgeExtractionWorkflow | Temporal cron 30 3 * * * | Minutes | extract_all_missions (extract learnings from recent executions) | Max 1 attempt |
| KnowledgeOfficerWorkflow | Temporal cron 0 4 * * * | Minutes | run_knowledge_officer (cross-mission insight promotion) | Max 1 attempt |
| EmbeddingBackfillWorkflow | Temporal cron 15 4 * * * | Minutes | backfill_embeddings (generate vectors for unembedded entries) | Max 1 attempt |

**Scheduled Jobs:**

> Extract from: main.py lifespan (Temporal schedule registration), maintenance_workflow.py, knowledge_workflow.py, scheduled_workflow.py.

| Schedule ID | Workflow | Interval/Cron | Purpose |
|------------|---------|---------------|---------|
| mission-plan-dispatch-schedule | ScheduledMissionCheckWorkflow | Configurable interval (dispatch_check_interval_seconds) | Find plans with approved tasks and start MissionRunWorkflow for each |
| maintenance-daily-cleanup | DailyCleanupWorkflow | `0 2 * * *` (02:00 UTC daily) | Purge expired sessions, stale data |
| maintenance-hourly-health-check | HourlyHealthCheckWorkflow | `0 * * * *` (top of every hour) | PostgreSQL and Redis connectivity checks |
| maintenance-weekly-report | WeeklyReportWorkflow | `0 6 * * 0` (Sunday 06:00 UTC) | Generate platform usage summary |
| maintenance-metrics-aggregation | MetricsAggregationWorkflow | `*/15 * * * *` (every 15 minutes) | Aggregate platform metrics (missions, sessions, costs) |
| maintenance-cleanup-environments | CleanupEnvironmentsWorkflow | `0 * * * *` (top of every hour) | Remove stale/idle Docker execution environments |
| maintenance-workflow-schedules | EvaluateWorkflowSchedulesWorkflow | `* * * * *` (every minute) | Evaluate cron-based user workflow schedules and trigger due runs |
| knowledge-daily-extraction | KnowledgeExtractionWorkflow | `30 3 * * *` (03:30 UTC daily) | Extract learnings from all missions' recent executions |
| knowledge-officer-daily | KnowledgeOfficerWorkflow | `0 4 * * *` (04:00 UTC daily) | Run Knowledge Officer agent for cross-mission insight promotion |
| knowledge-embedding-backfill | EmbeddingBackfillWorkflow | `15 4 * * *` (04:15 UTC daily) | Generate embeddings for learnings/decisions/milestones without vectors |

### 4.25 Observability and Monitoring

> Extract from: logging configuration, metrics emission, tracing instrumentation, health check endpoints, dashboard definitions.

| ID | Requirement | Acceptance Criteria | Source Reference |
|----|-------------|-------------------|-----------------|
| FR-124 | The system shall provide structured logging (JSON to file + console), health check endpoints (PostgreSQL + Redis), request context propagation (X-Request-ID, X-Frontend-ID), and response timing headers | Health /ready returns 503 when dependencies are down; all log entries include request_id when available; X-Response-Time header present on all responses | `core/logging.py`, `core/middleware.py`, `api/health.py` |

---

## 5.0 Non-Functional Requirements

### 5.1 Performance

| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-001 | Database operations shall complete within a configurable timeout | 10 seconds (application.yaml: timeouts.database) | `core/config_schema.py` TimeoutsSchema |
| NFR-002 | External API calls (LLM providers, search) shall complete within a configurable timeout | 30 seconds (application.yaml: timeouts.external_api) | `core/config_schema.py` TimeoutsSchema |
| NFR-003 | Background tasks shall complete within a configurable timeout | 120 seconds (application.yaml: timeouts.background) | `core/config_schema.py` TimeoutsSchema |
| NFR-004 | Agent execution activities shall have a configurable minimum timeout floor | 600 seconds (temporal.yaml: min_activity_timeout_seconds) | `core/config_schema.py` TemporalSchema |
| NFR-005 | Human approval gates shall time out after a configurable period | 14,400 seconds / 4 hours (temporal.yaml: approval_timeout_seconds) | `core/config_schema.py` TemporalSchema |
| NFR-006 | Escalation timeouts shall be configurable | 86,400 seconds / 24 hours (temporal.yaml: escalation_timeout_seconds) | `core/config_schema.py` TemporalSchema |
| NFR-007 | Shell command execution (run_command tool) shall enforce a per-invocation timeout | 120 seconds default (tools/run_command.py: timeout_seconds) | `tools/run_command.py` |
| NFR-008 | URL content extraction shall enforce a fetch timeout | 15 seconds (tools/fetch_url.py: timeout_seconds) | `tools/fetch_url.py` |
| NFR-009 | Plan refinement polling shall have a configurable maximum wait | 60 seconds (temporal.yaml: refine_poll_max_seconds) | `core/config_schema.py` TemporalSchema |
| NFR-010 | API pagination shall enforce configurable default and maximum page sizes | Default 50, max 100 (application.yaml: pagination) | `core/config_schema.py` PaginationSchema |
| NFR-011 | Request body size shall be capped | 1,048,576 bytes / 1MB (security.yaml: request_limits.max_body_size_bytes) | `core/config_schema.py` RequestLimitsSchema |
| NFR-012 | Task output size shall be capped for persistence | 1,048,576 bytes / 1MB (executions.yaml: max_task_output_size_bytes) | `core/config_schema.py` ExecutionsSchema |
| NFR-013 | Thinking trace persistence shall be capped | 50,000 characters (executions.yaml: max_thinking_trace_length) | `core/config_schema.py` ExecutionsSchema |
| NFR-014 | URL content extraction length shall be capped | 50,000 characters (research.yaml: extraction.max_content_length) | `research.yaml` |

### 5.2 Scalability

| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-015 | Database connection pool shall be configurable with pool size and overflow | Pool: 10, overflow: 20 (database.yaml) | `core/config_schema.py` DatabaseSchema |
| NFR-016 | Connection pool timeout shall be configurable | 30 seconds (database.yaml: pool_timeout) | `core/config_schema.py` DatabaseSchema |
| NFR-017 | Connection pool recycling shall be configurable to prevent stale connections | 1,800 seconds / 30 min (database.yaml: pool_recycle) | `core/config_schema.py` DatabaseSchema |
| NFR-018 | Concurrent mission execution shall be capped | 10 missions (workflows.yaml: max_concurrent_missions) | `core/config_schema.py` WorkflowsSchema |
| NFR-019 | Workflow steps per definition shall be capped | 20 steps (workflows.yaml: max_steps_per_workflow) | `core/config_schema.py` WorkflowsSchema |
| NFR-020 | Scheduled workflow runs per day shall be capped | 50 runs (workflows.yaml: max_scheduled_runs_per_day) | `core/config_schema.py` WorkflowsSchema |
| NFR-021 | Missions per owner shall be capped | 50 missions (missions.yaml: max_missions_per_owner) | `core/config_schema.py` MissionsSchema |
| NFR-022 | Concurrent web searches shall be capped | 10 searches (research.yaml: search.max_concurrent_searches) | `research.yaml` |
| NFR-023 | Embedding backfill shall process in configurable batches | 50 per batch (missions.yaml: knowledge.embedding.backfill_batch_size) | `core/config_schema.py` KnowledgeEmbeddingSchema |
| NFR-024 | Redis event streams shall enforce configurable max length | 10,000 entries (events.yaml: streams.default.maxlen) | `core/config_schema.py` EventsStreamSchema |
| NFR-025 | Supervisor autonomy loop shall have a hard iteration cap | 25 iterations (temporal.yaml: max_supervisor_iterations) | `core/config_schema.py` TemporalSchema |

### 5.3 Availability and Resilience

| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-026 | Agent execution activities shall retry on transient failure with configurable attempts | 2 attempts (temporal.yaml: execution_retry_max_attempts) | `core/config_schema.py` TemporalSchema |
| NFR-027 | Execution retry backoff shall be configurable (initial and max interval) | Initial: 5s, max: 60s (temporal.yaml) | `core/config_schema.py` TemporalSchema |
| NFR-028 | Persistence activities shall retry with configurable attempts | 3 attempts (temporal.yaml: persistence_retry_max_attempts) | `core/config_schema.py` TemporalSchema |
| NFR-029 | General Temporal activities shall retry with configurable attempts | 3 attempts (temporal.yaml: activity_retry_max_attempts) | `core/config_schema.py` TemporalSchema |
| NFR-030 | BudgetExceededError shall be non-retryable in Temporal workflows | Immediate workflow failure on budget exhaustion | `temporal/mission_run_workflow.py` |
| NFR-031 | Health readiness checks shall verify both PostgreSQL and Redis | /health/ready returns 503 if either is unreachable | `api/health.py` |
| NFR-032 | Workflow execution timeout shall be configurable at the horizon level | 30 days (temporal.yaml: workflow_execution_timeout_days) | `core/config_schema.py` TemporalSchema |
| NFR-033 | Session expiry and cleanup shall run at a configurable interval | 60 minutes (sessions.yaml: cleanup_interval_minutes) | `core/config_schema.py` SessionsSchema |
| NFR-034 | Dead letter queue shall be configurable for failed event processing | Enabled, prefix "dlq" (events.yaml) | `core/config_schema.py` EventsSchema |
| NFR-035 | Idempotency cache shall have a configurable TTL | 86,400 seconds / 24 hours (core/idempotency.py) | `core/idempotency.py` |
| NFR-036 | MCD in-memory cache shall have a configurable TTL | 30 seconds (services/mission_context.py) | `services/mission_context.py` |
| NFR-037 | Graceful server shutdown shall enforce a timeout for draining SSE connections | 5 seconds (services.py: --timeout-graceful-shutdown) | `services.py` |

**Error Code Taxonomy:**

> Extract from: core/exceptions.py. Each exception carries a structured error code. Exception handlers in exception_handlers.py map these to HTTP responses.

| Exception | Error Code | HTTP Status | Description |
|-----------|-----------|-------------|-------------|
| NotFoundError | RES_NOT_FOUND | 404 | Resource not found |
| ValidationError | VAL_VALIDATION_ERROR | 422 | Input validation failure (carries details dict) |
| AuthenticationError | AUTH_UNAUTHORIZED | 401 | Missing or invalid credentials |
| AuthorizationError | AUTHZ_FORBIDDEN | 403 | Insufficient permissions |
| ConflictError | RES_CONFLICT | 409 | State conflict (duplicate, version mismatch) |
| ExternalServiceError | SYS_EXTERNAL_SERVICE_ERROR | 502 | External service call failure |
| RateLimitError | RATE_LIMITED | 429 | Rate limit exceeded |
| BudgetExceededError | COST_BUDGET_EXCEEDED | 402 | Cost budget exhausted (carries current_cost, budget) |
| DatabaseError | SYS_DATABASE_ERROR | 500 | Database operation failure |
| ModelError | SYS_MODEL_ERROR | 502 | LLM model call failure (carries model_name, status_code) |
| ConfigurationError | SYS_CONFIGURATION_ERROR | 500 | Missing or invalid configuration |

**Failure Modes:**

| Failure Scenario | Expected Behaviour | Recovery Mechanism |
|-----------------|-------------------|-------------------|
| LLM provider unavailable | ModelError raised with model_name and status_code | |
| Database connection lost | DatabaseError; crash recovery on restart | |
| Message broker unreachable | Event publishing fails silently (best-effort); core operations continue | |
| Workflow engine down | Temporal durable execution resumes when engine recovers | |
| Agent timeout exceeded | Agent killed; task marked failed with timeout reason | |
| Redis outage during idempotency | Fail-open — request proceeds without cache (re-execute) | |
| Concurrent MCD update | Optimistic concurrency detects conflict; ConflictError returned | |

### 5.4 Maintainability

| ID | Requirement | Rationale |
|----|-------------|-----------|
| NFR-038 | Source files shall not exceed 1,000 lines; target ~400-500 lines | Maintainability; enforced by compliance scanner rule `file_size_limit` |
| NFR-039 | Backend shall enforce strict layering: API → Service → Repository → Model | Separation of concerns; no layer skipping |
| NFR-040 | All configuration shall be externalized to YAML + .env files; no hardcoded values in code | Deployment flexibility; compliance scanner rules `no_hardcoded_values`, `no_os_getenv_fallback` |
| NFR-041 | YAML config schemas shall use `extra="forbid"` — unknown keys cause immediate validation errors | Fail-fast on misconfiguration |
| NFR-042 | All logging shall use centralized `get_logger(__name__)` — no direct `import logging` | Consistent structured logging; compliance scanner rule `no_direct_logging` |
| NFR-043 | All imports shall be absolute — no relative imports | Maintainability; compliance scanner rule `no_relative_imports` |
| NFR-044 | `__init__.py` files shall contain only docstrings and necessary exports | No business logic in package init files |
| NFR-045 | Agent definitions shall be pluggable via YAML config + prompt files on disk | New agents added without code changes to the orchestration layer |
| NFR-046 | Tools shall be auto-discoverable via TOOL_DEF + register() convention | New tools added by creating a module in tools/ — no manual registration |
| NFR-047 | External dependencies (LLM providers, search providers) shall be wrapped behind platform interfaces | No direct provider SDK calls in business logic |

---

## 6.0 Data Requirements

### 6.1 Persistence Model

> Full entity-level detail (fields, types, constraints) is in the companion appendix: `docs/98-research/029a-domain-model-reference.md` (generated from model extraction).

| Entity | Table | Storage Type | Retention Policy | Key Indexes |
|--------|-------|-------------|-----------------|-------------|
| Mission | `missions` | PostgreSQL | Indefinite (archivable) | `name` (unique), `status`, `owner_id` |
| MissionMember | `mission_members` | PostgreSQL | Cascade on mission delete | `(mission_id, user_id)` unique |
| MissionContext (MCD) | `mission_contexts` | PostgreSQL (JSON column) | Indefinite; versioned | `mission_id` |
| ContextChange | `context_changes` | PostgreSQL | Indefinite (audit trail) | `context_id`, `version` |
| ContextMilestone | `context_milestones` | PostgreSQL | Indefinite | `mission_id` |
| MissionIntent | `mission_intents` | PostgreSQL | Indefinite; versioned | `mission_id`, `is_current` |
| MissionLearning | `mission_learnings` | PostgreSQL | Indefinite; promotable | `mission_id`, `domain_tags` |
| MissionEvent | `mission_events` | PostgreSQL | Indefinite (append-only) | `mission_id`, `event_type`, `session_id` |
| MissionArtifact | `mission_artifacts` | PostgreSQL | Indefinite | `mission_id`, `execution_record_id` |
| MissionPlan | `mission_plans` | PostgreSQL (JSON plan) | Indefinite | `mission_id`, `status` |
| MissionHistory / Decisions / Milestones | `mission_decisions`, `milestone_summaries` | PostgreSQL | Indefinite (compressed) | `mission_id` |
| Session | `sessions` | PostgreSQL | Configurable TTL; expirable | `mission_id`, `user_id`, `status` |
| SessionMessage | `session_messages` | PostgreSQL | Tied to session lifecycle | `session_id`, `created_at` |
| SessionChannel | `session_channels` | PostgreSQL | Tied to session lifecycle | `(channel_type, channel_id)` |
| SessionFollowup | `session_followups` | PostgreSQL | Expiry TTL (24h default) | `session_id`, `consumed_at` |
| ExecutionRecord | `execution_records` | PostgreSQL | Indefinite; summarizable | `mission_id`, `status`, `completed_at` |
| TaskExecution | `task_executions` | PostgreSQL | Tied to execution record | `execution_record_id`, `task_id` |
| TaskAttempt | `task_attempts` | PostgreSQL | Tied to task execution | `task_execution_id`, `status` |
| WorkflowDefinition | `workflow_definitions` | PostgreSQL | Indefinite | `name`, `mission_id` |
| WorkflowRun | `workflow_runs` | PostgreSQL | Indefinite | `workflow_definition_id`, `mission_id` |
| WorkflowSchedule | `workflow_schedules` | PostgreSQL | Indefinite | `mission_id`, `workflow_definition_id` |
| AgentCatalog | `agent_catalog` | PostgreSQL | Indefinite | `agent_name` (unique) |
| PromptVersion | `prompt_versions` | PostgreSQL | Indefinite (versioned) | `agent_catalog_id`, `version` |
| User | `users` | PostgreSQL | Indefinite | `email` (unique) |
| ApiKey | `api_keys` | PostgreSQL | Optional expiry | `key_prefix`, `(user_id, name)` unique |
| LLMCall | `llm_calls` | PostgreSQL | Indefinite | `execution_record_id`, `agent_name` |
| ModelPricing | `model_pricing` | PostgreSQL | Indefinite | `model_name` |
| Embedding | `embeddings` | PostgreSQL (pgvector) | Indefinite; versioned | `(source_table, source_id)`, vector index |
| HumanInputRequest | `human_input_requests` | PostgreSQL | Tied to workflow run | `workflow_run_id`, `status` |
| ExecutionEnvironment | `execution_environments` | PostgreSQL | Cleanup via scheduled job | `mission_id`, `status` |

### 6.2 Data Formats

| Format | Purpose | Schema Location |
|--------|---------|----------------|
| JSON (PostgreSQL column) | MCD context_data, plan DAG, event data, tool args/results | `models/*.py` — JSON/JSONB columns |
| JSON (API request/response) | All REST API communication | `schemas/*.py` — Pydantic models |
| JSON (Redis) | Event payloads, idempotency cache, SSE messages | `events/types.py`, `core/idempotency.py` |
| YAML (config) | Application settings, agent definitions, workflow templates | `config/settings/*.yaml`, `config/agents/**/*.yaml` |
| Markdown (prompts) | Agent system prompts, organization prompts | `config/prompts/**/*.md` |
| Markdown (code map) | Structural codebase overview | `.codemap/map.md` |
| SARIF (security) | Security scanner findings | `agents/security/sarif.py` |
| JSONL (logging) | Structured log output | `var/logs/system.jsonl` |

### 6.3 State Management

| State Type | Scope | Lifetime | Storage Mechanism Required |
|-----------|-------|----------|--------------------------|
| MCD (Mission Context) | Per mission | Mission lifetime (months/years) | Persistent store with versioning, optimistic concurrency |
| MCD in-memory cache | Per mission | 30s TTL | Process-local cache with TTL expiry |
| Session state | Per session | Configurable TTL (hours/days) | Persistent store with status machine |
| Rolling summary | Per session | Session lifetime | Persistent store; anchor + version tracking |
| Temporal workflow state | Per workflow execution | Until workflow completes or times out | Durable workflow engine with signal/query support |
| Idempotency cache | Per request | 24h TTL | Key-value store with TTL |
| SSE token | Per browser connection | 30s TTL (single-use) | Key-value store with atomic get-and-delete |
| Event bus channels | Per mission | Ephemeral (pub/sub) | Pub/sub messaging with channel scoping |
| Agent FileScope | Per agent invocation | Request lifetime | In-memory (constructed from config at dispatch time) |

### 6.4 Data Migration and Seeding

| Operation | Mechanism | Reversible | Source |
|-----------|-----------|-----------|--------|
| Schema migrations | Alembic (autogenerate + manual) | Yes (downgrade) | `modules/backend/migrations/versions/` |
| Agent catalog seeding | `admin.py admin seed-catalog` → `CatalogSeederService` | Yes (--force re-seeds) | `config/agents/**/*.yaml` |
| Workflow template seeding | Auto on startup + `admin.py admin seed-workflows` | Yes (--force re-seeds) | `config/workflow_templates/*.yaml` |
| Dev user seeding | Auto on `services.py start` + `admin.py admin seed-dev-users` | No (idempotent creates) | `BootstrapService.ensure_dev_users()` |
| System account seeding | `admin.py admin ensure-system-accounts` | No (idempotent creates) | `BootstrapService` |
| First admin bootstrap | `admin.py admin bootstrap-user` | No | Interactive (email + password) |

---

## 7.0 Integration Requirements

> Extract from: client libraries, SDK usage, API calls, webhook handlers, event publishers/subscribers.

| ID | Integration | Protocol | Data Contract | Error Handling | Source Reference |
|----|-------------|----------|--------------|----------------|-----------------|
| IR-001 | Anthropic API | HTTPS REST | Chat completion JSON; tool_use blocks; structured output | ModelError wrapping provider SDK exceptions | `core/model_providers.py`, `core/model_builder.py` |
| IR-002 | OpenAI API | HTTPS REST | Chat completion JSON; function calling; JSON mode | ModelError wrapping provider SDK exceptions | `core/model_providers.py` |
| IR-003 | GitHub Copilot SDK | HTTPS OAuth + REST | Chat completion; prompted structured output (no native JSON mode) | Session lifecycle cleanup; JSON repair fallback | `core/copilot_model.py` |
| IR-004 | Perplexity API | HTTPS REST | Search query → ranked results with snippets | Fallback to next provider in chain | `services/search/web_search.py` |
| IR-005 | Tavily API | HTTPS REST | Search query → results with content extraction | Fallback to next provider in chain | `services/search/web_search.py` |
| IR-006 | Brave Search API | HTTPS REST | Search query → results | Terminal in provider chain | `services/search/web_search.py` |
| IR-007 | Telegram Bot API | HTTPS webhook + polling | Message/callback payloads; webhook verification | Graceful failure; notifications best-effort | `clients/telegram/` |
| IR-008 | PostgreSQL | TCP (asyncpg) | SQL; async session pool; Alembic migrations | DatabaseError; connection pool with timeout/overflow | `core/database.py` |
| IR-009 | Redis | TCP (redis-py async) | Pub/sub channels; key-value with TTL | Fail-open for caching/idempotency; fail-hard for event bus | `events/bus.py`, `core/idempotency.py` |
| IR-010 | Temporal Server | gRPC | Workflow/activity definitions; signals; queries; schedules | Durable execution survives restarts; ScheduleAlreadyRunningError ignored | `temporal/client.py` |
| IR-011 | Jina Reader API | HTTPS REST | URL → extracted markdown content | Fallback after trafilatura/readability-lxml | `services/search/content_extract.py` |

### 7.1 LLM Provider Integration

> Extract from: `core/model_builder.py`, `core/model_providers.py`, `core/copilot_model.py`, `schemas/agent_config.py` (AgentModelSchema).

| ID | Requirement | Source Reference |
|----|-------------|-----------------|
| IR-012 | The system shall support 3 LLM providers (Anthropic, OpenAI, GitHub Copilot) with per-agent model pinning | `AgentModelSchema.name` prefix determines provider |
| IR-013 | Model selection shall be determined by agent config — no runtime model override path exists | `AgentModelSchema` docs: "Model upgrades are agent version bumps" |
| IR-014 | The system shall enforce per-agent token limits (max_tokens) as a non-overridable property | `AgentModelSchema.max_tokens`, `AgentConfigSchema.max_tokens` |
| IR-015 | The system shall support Anthropic prompt caching with configurable TTL per agent | `AgentConfigSchema.cache_ttl`, `CostSchema.default_cache_ttl` |
| IR-016 | The system shall support Anthropic extended thinking for compatible models | `AgentConfigSchema.thinking_budget`, model name detection in `model_builder.py` |
| IR-017 | The system shall support structured output via native JSON schema (Anthropic/OpenAI) or prompted mode (Copilot) | `CopilotModel` forces `supports_json_schema_output=False` |
| IR-018 | Provider credentials shall be loaded from environment variables configured in `providers.yaml` | `ProviderAuthSchema.env_var` |

**Provider Abstraction Requirements:**

| Capability | Required | Description |
|-----------|----------|-------------|
| Chat completion | Yes | All three providers support basic chat completion |
| Streaming responses | Yes | Anthropic/OpenAI native streaming; Copilot wraps final response as single chunk |
| Tool/function calling | Yes | All three providers; Copilot uses custom ToolBridge adapter |
| Structured output | Yes | Native JSON schema for Anthropic/OpenAI; prompted mode for Copilot with optional json_repair |
| Vision/multimodal | No | Not currently used by any agent |
| Embeddings | Yes (separate) | Via `core/embeddings.py`; not through the model_builder path; used for knowledge platform vector search |

### 7.2 Event Bus Integration

> Extract from: `events/bus.py`, `events/types.py`, Redis pub/sub channels.

| ID | Requirement | Source Reference |
|----|-------------|-----------------|
| IR-019 | The event bus shall publish JSON-serialised events to Redis pub/sub channels scoped by mission | `SessionEventBus.publish_to_mission` → channel `mission_events:{mission_id}` |
| IR-020 | Events not in the configurable exclude list shall be persisted to the mission_events table | `events.yaml` → `exclude_event_types`; `bus.py` `_persist_event` |
| IR-021 | Event consumers shall support filtered subscription (by event type list) | `subscribe_mission_filtered` |
| IR-022 | Event deserialisation shall reconstruct typed event models via EVENT_TYPE_MAP registry | `deserialize_event` → class lookup by `event_type` string |

**Event Channels:**

| Channel Pattern | Publisher | Subscriber(s) | Delivery Guarantee |
|----------------|-----------|---------------|-------------------|
| `mission_events:{mission_id}` | SessionEventBus | SSE streams, TUI event listener, CLI console | At-most-once (Redis pub/sub); persisted copy in DB for durability |
| `notify:mission:{mission_id}` | RunNotifierService | Frontend notification subscriber | At-most-once; best-effort |

---

## 8.0 Security Requirements

> Extract from: core/security.py, core/dependencies.py, core/idempotency.py, services/auth.py, services/user.py, models/user.py, models/api_key.py, gateway/security/, middleware.py, agents/deps/base.py (FileScope), agents/mission_control/middleware.py (guardrails), config/settings/security.yaml.

### 8.1 Human Authentication

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-001 | The system shall support JWT Bearer token authentication with configurable expiry | Authentication | |
| SR-002 | The system shall support API key authentication with bcrypt-hashed storage | Authentication | |
| SR-003 | API keys shall use prefix lookup followed by bcrypt verification (never stored in plaintext) | Authentication | |
| SR-004 | JWT tokens shall include audience validation, expiry, and token type (access vs refresh) | Authentication | |
| SR-005 | SSE streaming endpoints shall use short-lived single-use tokens (Redis-backed, 30s TTL) | Authentication | |
| SR-006 | SSE endpoints shall NOT accept API keys (only JWT/SSE tokens) — API keys lack time-scoped expiry | Authentication | |

**Authentication Resolution Order:**

| Priority | Method | Mechanism |
|----------|--------|-----------|
| 1 | Authorization: Bearer \<jwt\> | Decode JWT → load user by 'sub' claim |
| 2 | X-API-Key header | Prefix lookup + bcrypt verify → load user |
| 3 | Legacy ADMIN_API_KEY | Backwards compatibility fallback |

### 8.2 Role-Based Access Control (RBAC)

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-007 | The system shall enforce a hierarchical role system with configurable role levels | Authorisation | |
| SR-008 | Role definitions (name, level, description) shall be loaded from security.yaml, not hardcoded | Authorisation | |
| SR-009 | Admin endpoints shall require admin role via a dedicated dependency | Authorisation | |
| SR-010 | Telegram/external user IDs shall be mappable to roles via config (security.user_roles) | Authorisation | |
| SR-011 | Unknown users shall default to the lowest role (viewer), not admin | Authorisation | |

**User Model Fields:**

| Field | Description |
|-------|-------------|
| system_role | Platform-wide role (sysadmin, admin, user) |
| email | Unique identifier for authentication |
| hashed_password | bcrypt hash |
| is_active | Account enable/disable flag |

### 8.3 Mission-Level Access Control

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-012 | Each mission shall have membership with role-based permissions (owner, maintainer, viewer) | Authorisation | |
| SR-013 | Mission tier (user vs platform) shall restrict which agents and operations are available | Authorisation | |
| SR-014 | Platform-tier missions shall require admin role to create and access | Authorisation | |

### 8.4 Agent Security Boundaries

> Extract from: agents/deps/base.py (FileScope), tools/, agents/config_schema.py (InvocationSchema), middleware.py (guardrails), agents/security/.

| ID | Requirement | Source Reference |
|----|-------------|-----------------|
| SR-015 | Each agent's filesystem access shall be constrained by a per-agent FileScope (read/write path allowlists) | |
| SR-016 | FileScope enforcement shall occur in every tool implementation before any I/O operation — no tool shall bypass scope checks | |
| SR-017 | Agent invocation tiers (user/platform/system) shall be enforced at dispatch time | |
| SR-018 | Input guardrails shall block prompt injection patterns before any LLM call | |
| SR-019 | Guardrail injection patterns shall be configurable via YAML, not hardcoded | |
| SR-020 | Agent cost ceilings (max_budget_usd) shall be enforced per invocation — exceeding agents are terminated | |
| SR-021 | Agent models shall be pinned and non-overridable at runtime (model changes require a version bump) | |
| SR-022 | Horizontal (supervisory) agents shall have explicit allowed_agents sets and max_delegation_depth | |
| SR-023 | MCD guardrail entries shall be immutable by agents — only humans can remove guardrails | |

**Agent Security Layers:**

| Layer | What It Constrains | Enforcement Point |
|-------|-------------------|------------------|
| FileScope | Which files an agent can read/write | Tool implementations (check_read, check_write) |
| Invocation tier | Who can invoke the agent | Dispatch/routing layer |
| Input guardrails | What text reaches the LLM | Middleware (check_guardrails) |
| Cost ceiling | How much an agent can spend | Cost tracking per invocation |
| Model pinning | Which LLM the agent uses | Agent config (non-overridable) |
| Delegation constraints | Which agents can be delegated to | HorizontalAgentDeps |
| Execution environment | Where the agent runs (local/container) | ExecutionEnvironmentService |
| Interface contract | What inputs/outputs are valid | Plan validator |

### 8.5 Input Validation and Secrets

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-024 | All secrets shall be loaded from environment variables or .env — never from YAML configs | Secrets Management | |
| SR-025 | Missing required secrets shall cause startup failure, not silent fallback | Secrets Management | |
| SR-026 | All external inputs shall be validated via Pydantic schemas before processing | Input Validation | |
| SR-027 | CORS origins shall be configurable and deny-all when empty | Transport Security | |

### 8.6 Idempotency

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-028 | Mutating API endpoints that are expensive to duplicate shall support idempotency keys | Resilience | |
| SR-029 | Idempotency responses shall be cached in Redis with configurable TTL (default 24h) | Resilience | |
| SR-030 | Idempotency shall fail-open on Redis outage (re-execute rather than block) | Resilience | |

### 8.7 Rate Limiting

| ID | Requirement | Category | Source Reference |
|----|-------------|----------|-----------------|
| SR-031 | Per-channel rate limits shall be configurable and enforced at the gateway layer | Rate Limiting | |
| SR-032 | Rate limit exceeded shall raise RateLimitError with appropriate HTTP status | Rate Limiting | |

**Decision Boundaries:**

> Extract from: gate system, approval flows, agent constraints, MCD guardrails.

| Decision Type | Boundary | Enforcement Mechanism |
|--------------|----------|----------------------|
| Code modification | FileScope write allowlist | PermissionError at tool level |
| Shell execution | Execution environment mode | Container/worktree isolation |
| Budget spend | Per-agent ceiling + mission budget | BudgetExceededError |
| Plan approval | Gate mode (off/interactive/ai_assisted/autonomous) | Gate system |
| MCD guardrail removal | Agent-immutable | ContextCurator rejection |
| Agent delegation | allowed_agents + max_depth | HorizontalAgentDeps |
| Platform operations | Invocation tier + mission tier | Dispatch routing |

---

## 9.0 Configuration and Environment

> Extract from: environment variable references, config files, settings classes, feature flags.

> **Full configuration detail** (every environment variable, every YAML parameter across 20 config files, every feature flag) is in the companion appendix:
>
> **[029c-configuration-reference.md](029c-configuration-reference.md)**

### 9.1 Environment Variables

11 secrets loaded from `config/.env` via Pydantic Settings. 7 required (DB_PASSWORD, REDIS_PASSWORD, JWT_SECRET, API_KEY_SALT, TELEGRAM_BOT_TOKEN, TELEGRAM_WEBHOOK_SECRET, ANTHROPIC_API_KEY). 4 optional with empty-string defaults (ADMIN_API_KEY, PERPLEXITY_API_KEY, TAVILY_API_KEY, BRAVE_API_KEY). All sensitive.

### 9.2 Feature Flags

23 boolean flags in `config/settings/features.yaml` controlling auth behaviour, channel adapters, gateway subsystems, agent features, security checks, and experimental capabilities. Key flags:

| Flag | Default | Impact |
|------|---------|--------|
| security_startup_checks_enabled | true | Blocks startup on weak secrets/unsafe production config |
| channel_telegram_enabled | true | Mounts Telegram webhook adapter |
| experimental_mission_plan_daemon_enabled | true (YAML) / False (schema) | Enables autonomous plan dispatch — **spends budget** |
| auth_require_api_authentication | true | Requires JWT/API key on protected routes |
| auth_allow_self_registration | false | Self-service signup |

### 9.3 Configurable Parameters

20 YAML config files under `config/settings/` covering: application, database, logging, security, gateway, events, sessions, executions, temporal, workflows, notifications, missions, gate, providers, cost, research, security_assessment, environments, features, test. 300+ individual parameters. Key domains:

| Domain | File | Key Parameters |
|--------|------|---------------|
| Server | application.yaml | host, port, CORS origins, timeouts, pagination limits |
| Database | database.yaml | Pool size (10), overflow (20), timeout (30s), recycle (1800s) |
| Auth | security.yaml | JWT algorithm (HS256), access TTL (30m), refresh TTL (7d), role levels |
| Sessions | sessions.yaml | Default TTL (24h), max TTL (168h), budget ($50 default, $500 max) |
| Temporal | temporal.yaml | 21 parameters controlling timeouts, retries, polling, supervisor iterations |
| Missions | missions.yaml | MCD size caps (20KB max), knowledge platform settings, embedding config |
| Gate | gate.yaml | Mode (off), 6 checkpoint points, AI reviewer model, auto-skip rules |
| Cost | cost.yaml | Cache TTL (1h), hourly spend alert ($5), emergency downgrade model |
| Workflows | workflows.yaml | Max steps (20), default budget ($10), max budget ($100), schedule limits |
| Notifications | notifications.yaml | Budget warning threshold (80%), channel routing |

**Note:** `research.yaml` and `environments.yaml` exist on disk but are **not wired into AppConfig** — no schema validation or load path in `config.py`.

---

## 10.0 Migration Traceability

> This section maps current Python modules to the requirements they satisfy. It exists to help the target implementation team understand where each requirement was derived from, without anchoring them to the source architecture.

### 10.1 Module-to-Requirement Map

| Python Module/Package | Primary Purpose | Requirements Satisfied |
|----------------------|----------------|----------------------|
| `modules/backend/core/config.py` | Configuration loading (YAML + .env) | 9.1, 9.2, 9.3 |
| `modules/backend/core/security.py` | JWT, password hashing, API keys, RBAC | SR-001–SR-011 |
| `modules/backend/core/dependencies.py` | Auth dependencies, idempotency | SR-001–SR-006, SR-028–SR-030 |
| `modules/backend/core/exceptions.py` | Exception hierarchy with error codes | NFR (5.3 error taxonomy) |
| `modules/backend/core/middleware.py` | Request context (X-Request-ID, source) | NFR (observability) |
| `modules/backend/core/database.py` | Async SQLAlchemy engine and sessions | IR-008 |
| `modules/backend/core/model_builder.py` | LLM model construction per agent config | IR-012–IR-018 |
| `modules/backend/core/model_providers.py` | Provider factory (Anthropic, OpenAI, Copilot) | IR-012–IR-018 |
| `modules/backend/core/copilot_model.py` | PydanticAI Model for GitHub Copilot SDK | IR-003, IR-017 |
| `modules/backend/core/embeddings.py` | Embedding generation for knowledge search | 4.2.8 |
| `modules/backend/core/agent_registry.py` | Agent config loading from YAML | 4.11 |
| `modules/backend/services/mission.py` | Mission CRUD, membership, lifecycle | 4.1, SR-012–SR-014 |
| `modules/backend/services/mission_context.py` | MCD read/write, versioning, caching, milestones | 4.2.2 |
| `modules/backend/services/context_assembler.py` | Layered context assembly with token budget | 4.2.1 |
| `modules/backend/services/context_curator.py` | MCD update validation | 4.2.3 |
| `modules/backend/services/session.py` | Session lifecycle, cost, streaming | 4.4 |
| `modules/backend/services/session_memory.py` | Rolling summary compression | 4.2.4 |
| `modules/backend/services/session_followup.py` | Wake-on-event followups | 4.16 |
| `modules/backend/services/knowledge.py` | Unified knowledge search (structured/semantic/hybrid) | 4.2.7 |
| `modules/backend/services/history_query.py` | Execution history queries for context Layer 2 | 4.2.9 |
| `modules/backend/services/mission_plan.py` | Plan CRUD, task state management | 4.6 |
| `modules/backend/services/mission_plan_orchestrator.py` | Plan lifecycle orchestration (dispatch, budget, Temporal) | 4.6 |
| `modules/backend/services/mission_plan_crash_recovery.py` | Orphaned task state reconciliation on startup | 4.6 |
| `modules/backend/services/execution_persistence.py` | Execution record, task, attempt persistence | 4.6, 6.1 |
| `modules/backend/services/workflow_definition.py` | Workflow template CRUD | 4.13 |
| `modules/backend/services/workflow_run.py` | Workflow run lifecycle | 4.13 |
| `modules/backend/services/workflow_run_waves.py` | Wave execution engine with condition evaluation | 4.13, 4.21 |
| `modules/backend/services/workflow_schedule.py` | Cron-based workflow scheduling | 4.13 |
| `modules/backend/services/workflow_seeder.py` | Seed system workflow templates from YAML | 4.13 |
| `modules/backend/services/condition_evaluator.py` | Safe expression evaluator for workflow branching | 4.21 |
| `modules/backend/services/compliance.py` | Deterministic code compliance scanner | 4.22 |
| `modules/backend/services/cost_alerts.py` | Cost threshold alerting | 4.7 |
| `modules/backend/services/token_economics.py` | Token/cost computation | 4.7 |
| `modules/backend/services/run_notifier.py` | Lifecycle notifications (Telegram, SSE) | 4.17 |
| `modules/backend/services/summarization.py` | History compression into milestones | 4.20 |
| `modules/backend/services/branch_manager.py` | Git branch management for agent code changes | 4.19 |
| `modules/backend/services/agent_catalog.py` | Agent catalog CRUD | 4.11 |
| `modules/backend/services/mission_team.py` | Mission roster management | 4.11 |
| `modules/backend/services/auth.py` | Login, token refresh, API key management | SR-001–SR-006 |
| `modules/backend/services/user.py` | User CRUD with role enforcement | SR-007–SR-011 |
| `modules/backend/services/bootstrap.py` | First admin user and dev user seeding | 6.4 |
| `modules/backend/services/health.py` | PostgreSQL + Redis health checks | 4.25 |
| `modules/backend/services/console.py` | Mission event timeline queries | 4.18 |
| `modules/backend/services/execution_environment.py` | Container/worktree execution isolation | 4.10 |
| `modules/backend/services/container_backend.py` | Docker container management | 4.10 |
| `modules/backend/agents/mission_control/` | Orchestration engine (dispatch, routing, cost, gates, verification) | 4.3, 4.5, 4.6, 4.8 |
| `modules/backend/agents/mission_control/helpers/prompt_builder.py` | Five-layer prompt assembly | 4.2.6 |
| `modules/backend/agents/mission_control/middleware.py` | Input guardrails (injection pattern blocking) | SR-018–SR-019, 4.15.3 |
| `modules/backend/agents/mission_control/gate.py` | Gate evaluation (off/interactive/ai_assisted/autonomous) | 4.5 |
| `modules/backend/agents/mission_control/gate_post_run.py` | Post-run supervisor (8-branch decision matrix) | 4.5 |
| `modules/backend/agents/deps/base.py` | FileScope, BaseAgentDeps, HorizontalAgentDeps | SR-015–SR-016, SR-022, 4.14.2, 4.15.5 |
| `modules/backend/tools/` | Tool implementations (read, write, search, execute) | 4.14.3 |
| `modules/backend/tools/registry.py` | Tool auto-discovery and agent registration | 4.14.1 |
| `modules/backend/agents/adapters/` | Tool composition adapters | 4.14.4 |
| `modules/backend/events/bus.py` | Redis pub/sub event bus with DB persistence | IR-019–IR-022, 4.18 |
| `modules/backend/events/types.py` | All event type definitions (~50 types) | 4.23.2 |
| `modules/backend/temporal/` | Temporal workflow/activity definitions | 4.24, 4.16 (HumanInput) |
| `modules/backend/gateway/` | Channel adapter registry and security | 4.12, SR-031–SR-032 |

### 10.2 Implementation Notes

| Topic | Current Approach | Rationale (if known) | Carry Forward? |
|-------|-----------------|---------------------|---------------|
| Agent definition pattern | PydanticAI `Agent` with `RunContext` deps injection; YAML config per agent | Framework-specific but well-structured | No (framework-specific) |
| Structured output validation | PydanticAI native for Anthropic/OpenAI; prompted mode with json_repair for Copilot | Copilot SDK lacks native JSON mode | Requirement only (structured output must be validated) |
| Workflow orchestration model | Temporal durable workflows with signals/queries; `MissionRunWorkflow` autonomy loop | Temporal provides durability, retries, signals, and crash recovery | Requirement only (durable execution with signals) |
| Session transport abstraction | Redis pub/sub → SSE; shared sessions across CLI/TUI/frontend | Sessions are client-surface-agnostic by design | Yes (design pattern worth preserving) |
| Context assembly | Four-layer token-budgeted assembly in `ContextAssembler` | Priority order ensures critical context is never trimmed | Yes (design pattern — layered priority with budget) |
| MCD mutation model | JSON Patch-style ops (add/replace/remove) with dot-notation paths | Enables granular agent updates without full MCD replacement | Yes (design pattern) |
| Plan DAG execution | In-process dispatch loop (not Temporal activities per task); Temporal wraps the wave | Task execution is too fast and varied for individual Temporal activities | Implementation note (could change) |
| Agent execution engines | Four engines: pydantic_ai (in-process), claude_code (subprocess), codex (subprocess), copilot (in-process) | Multi-engine support enables best-of-breed model access | Requirement only (multi-engine support) |
| Event persistence | Selective: exclude list in YAML; events not excluded are persisted to `mission_events` | High-volume events (chunks, cost updates) excluded to avoid DB bloat | Yes (configurable exclusion pattern) |
| Supervisor pattern | Deterministic 8-branch matrix + LLM classifier only for ambiguous "plan exhausted" case | Minimises LLM cost; LLM used only when deterministic rules can't decide | Yes (design pattern — deterministic first, LLM fallback) |

### 10.3 Known Technical Debt

> See also `TODO.md` in the project root for the full findings list.

| Item | Location | Impact | Recommendation |
|------|----------|--------|---------------|
| mission_plan_orchestrator.py exceeds 1,388 lines | `services/mission_plan_orchestrator.py` | Maintainability; violates 1,000-line limit | Decompose into submodules (dispatch prep, plan lifecycle, budget enforcement) |
| director/agent.py exceeds 1,291 lines | `agents/mission/director/agent.py` | Maintainability | Extract tool fork handling, chat routing, plan refinement |
| workflow_run_waves.py exceeds 1,101 lines | `services/workflow_run_waves.py` | Maintainability | Extract human input handling, condition evaluation dispatch |
| GatewayRateLimiter not wired to production | `gateway/security/rate_limiter.py` | Rate limiting is not enforced | Wire into middleware or remove |
| Rate limiter field name mismatch | `rate_limiter.py` vs `security.yaml` | Configured limits not applied | Align field names (`messages_*` vs `requests_*`) |
| ApiKey.scopes column unused | `models/api_key.py` | False security assumption | Implement scope enforcement or remove field |
| Legacy ADMIN_API_KEY backdoor | `services/auth.py` | Security risk if env var leaks | Deprecate; migrate to per-user API keys |
| SSE token fail-open without Redis | `endpoints/auth.py` | Returns token even when Redis write fails | Fail request instead of silently continuing |
| Four exception classes never raised | `core/exceptions.py` | Incomplete error handling coverage | Wire into appropriate failure paths |
| Deprecated adapter re-exports | `agents/adapters/{code,research,filesystem}.py` | Dead code | Remove and update imports |
| Branch manager not imported anywhere | `services/branch_manager.py` | Dead code | Wire into dispatch for code-producing agents |
| Event type name mismatch in exclude config | `events.yaml` vs `events/types.py` | High-volume events may be persisted unnecessarily | Fix `session.cost.update` → `session.cost.updated` |
| events/types.py docstring contradicts bus.py | `events/types.py` | Misleading documentation | Update docstring to reflect actual persistence behaviour |
| admin.py bypasses API layer for 2 commands | `admin.py` (migrate-planning-agent, seed-workflows) | Layer violation | Delegate to service methods or admin API endpoints |

---

## Appendix A: Glossary

> Extract from: docstrings, README, domain model comments, AGENTS.md. Preserve exact terminology.

| Term | Definition |
|------|-----------|
| Mission | Long-lived domain primitive carrying brief, team, workflows, and accumulated context; outlives any session, run, or execution |
| MCD (Mission Context Document) | Versioned persistent JSON document per mission; accumulates knowledge across runs; synonym: PCD (Project Context Document) |
| Session | Transport primitive carrying real-time events, multi-channel bindings, and conversation history for a single execution |
| Agent | An AI actor with a defined role, system prompt, tools, model, and constraints; operates within a mission team roster |
| Roster | The set of agents assigned to a mission team for a specific execution |
| Tool | A discrete capability registered to an agent (filesystem, web search, code analysis, etc.) |
| Workflow | A reusable, ordered sequence of tasks seeded from YAML templates; system workflows are global, user workflows are mission-scoped |
| Workflow Definition | The template describing a workflow's steps, agents, and dependencies |
| Workflow Run | A single execution instance of a workflow definition |
| TaskPlan | A directed acyclic graph (DAG) of tasks produced by the planning agent for an objective |
| Gate | A human-in-the-loop approval checkpoint; modes: off, interactive, ai_assisted, autonomous |
| Execution Record | A persistent record of a single objective execution including plan, task results, cost, and status |
| Intent | The current strategic objective for a mission; versioned with revision history |
| Learning | An extracted pattern or insight from past runs; has kind, confidence, domain tags, and recommendation |
| Milestone | A named snapshot of MCD state or a compressed execution summary |
| Domain Tags | A controlled vocabulary used to scope context assembly, learnings, and search |
| Context Assembler | The pipeline that builds the complete context packet for an agent within a token budget |
| Context Curator | The validation layer between agent task results and MCD mutations |
| Rolling Summary | Compressed representation of a long session's message history; anchored and versioned |
| Verification Tier | The level of output validation applied to an agent's task result |
| Channel Adapter | A gateway component that adapts a client surface (Telegram, frontend, etc.) to the platform |
| Execution Environment | The runtime context for agent task execution (local process or isolated Docker container) |
| FileScope | Per-agent filesystem access control — configurable read/write path allowlists enforced by all filesystem tools |
| Invocation Tier | Access level required to invoke an agent: user (any authenticated), platform (admin only), system (infrastructure only) |
| Session Followup | A wake-on-event primitive — agents schedule a prompt to fire when a specified event occurs |
| Tool Registry | Auto-discovery mechanism that loads tool modules and registers them on agents at creation time |
| Agent Adapter | A composition layer that bundles related tools into thematic groups for agent registration |
| Guardrail | Input protection: configurable regex patterns that block prompt injection before any LLM call |
| Idempotency Guard | Redis-backed request deduplication via Idempotency-Key headers for expensive mutating operations |
| Condition Evaluator | Safe expression evaluator for workflow branching — supports @node/@context references, operators, and logic (no eval()) |
| Mission Event | A persisted, append-only audit log entry for narrative-relevant events in a mission's timeline |
| Human Input Request | An agent-initiated request for human input during execution, paused via Temporal workflow until answered |
| Compliance Scanner | Deterministic, rule-based Python code scanner with configurable rules and severity levels (no LLM dependency) |

---

## Appendix B: Extraction Checklist

Use this checklist to verify completeness of extraction.

**Core Domain:**
- [ ] All entry points identified (CLI, API, SSE/streaming, message handlers, TUI)
- [ ] All database models captured with fields, types, and constraints
- [ ] All enums and state machines documented with valid transitions
- [ ] All API endpoints documented with request/response schemas
- [ ] All agent types identified with their tools, models, and capabilities
- [ ] All workflow definitions captured with activities and compensation
- [ ] All integration points documented with protocols and contracts
- [ ] All environment variables and configuration parameters listed
- [ ] All security controls identified (auth, authz, validation, audit)
- [ ] All error handling patterns documented with failure modes

**Context and Memory (Section 4.2):**
- [ ] Context assembly pipeline documented with all layers, priority order, and trim rules
- [ ] MCD seed structure, mutation operations, restricted paths, and size caps captured
- [ ] Token budget parameters identified with defaults and config sources
- [ ] Session memory compression threshold and rolling summary mechanism documented
- [ ] All five knowledge surfaces enumerated with schemas and scoping rules
- [ ] All three search modes documented with cost/quality tradeoffs
- [ ] Prompt layering documented with all five layers and their sources
- [ ] Embedding lifecycle captured (generation, backfill schedule, versioning)
- [ ] History exclusion rules documented (summarized execution filtering)
- [ ] Context update flow documented end-to-end (agent result → MCD mutation → audit → cache invalidation)

**Execution and Control:**
- [ ] All gate modes and stages documented with approval flows
- [ ] Plan DAG lifecycle captured (creation, validation, execution, retry, crash recovery)
- [ ] All scheduled/cron jobs documented with intervals and purposes
- [ ] All event types documented with publishers and consumers
- [ ] All gate/approval flows documented with escalation paths
- [ ] All cost constraints and budget enforcement rules captured
- [ ] All verification tiers documented with selection criteria
- [ ] Research pipeline phases documented with agent roles

**Agent Security and Access Control (Sections 4.15, 8.0):**
- [ ] Agent invocation tiers documented (user/platform/system) with enforcement rules
- [ ] FileScope read/write allowlists documented per agent
- [ ] All tool implementations verified to enforce FileScope before I/O
- [ ] Input guardrail injection patterns documented with config source
- [ ] Agent cost ceilings and budget enforcement captured
- [ ] Model pinning policy documented (no runtime override)
- [ ] Horizontal delegation constraints documented (allowed_agents, max_depth)
- [ ] Human authentication methods documented (JWT, API key, SSE token)
- [ ] Role hierarchy and RBAC model documented with config source
- [ ] Mission membership model documented (owner/maintainer/viewer, tiers)
- [ ] Idempotency mechanism documented with TTL and fail-open policy

**Infrastructure:**
- [ ] Agent catalog seeding and versioning mechanism captured
- [ ] Multi-channel gateway adapter interface documented
- [ ] Execution environment modes (local vs container) documented
- [ ] Workflow scheduling (cron, cooldown, iteration limits) captured
- [ ] Session followup system documented (wake-on-event, caps, expiry)
- [ ] Notification system documented (channels, event types, thresholds)
- [ ] Mission event timeline (persisted audit log) documented
- [ ] Branch management naming convention and commit attribution documented
- [ ] Summarization pipeline documented (compression, exclusion marking)
- [ ] Condition evaluator syntax and operators documented
- [ ] Compliance scanner rules and configuration documented
- [ ] Error code taxonomy documented with HTTP status mappings
- [ ] Tool registry auto-discovery mechanism documented
- [ ] Agent adapter composition layer documented

**Quality:**
- [ ] Test files reviewed for implicit requirements
- [ ] TODO/FIXME/HACK comments captured as technical debt
- [ ] Hardcoded values extracted as configurable parameters
- [ ] Domain glossary complete and consistent with codebase terminology
