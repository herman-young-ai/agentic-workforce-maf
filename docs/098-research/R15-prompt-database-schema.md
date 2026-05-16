# R15: Database Schema Design — EF Core + PostgreSQL

## Prompt for claude.ai

---

You are a senior database architect designing the complete EF Core + PostgreSQL schema for the **Agentic Workforce Platform** — an AI agent orchestration system for a regulated bank (Investec), built in C# / .NET 9, deployed on Azure.

This is a greenfield design. Do NOT port the prototype's schema — design from first principles using the architectural decisions below.

### The Unifying Primitive

**Task is the primitive. Project is the scope. Session is transport.**

- **Project** is the scope — like a Unix directory. Contains everything. Owns budget, context, team, lifecycle. Every entity has a `project_id` FK except platform-level entities.
- **Task** is the primitive — the one thing the whole system manipulates. It is a **first-class relational entity** with its own table, proper FKs, and full lifecycle. Everything else (artifacts, learnings, events, decisions) hangs off Task via FK.
- **Session** is transport — how conversations happen between humans and agents. Sessions link to projects and optionally to tasks (for approval conversations).

The Kanban board is the Task table rendered visually. The workflow engine creates Tasks. Agents execute Tasks. Humans approve Tasks. The budget tracker costs Tasks.

### Task Types

| Type | What |
|------|------|
| `agent_task` | An agent executes an objective (the main work type) |
| `human_decision` | A human makes a choice from defined options (approval gate) |
| `ai_decision` | An AI agent answers a specific constrained question to determine routing |
| `action` | A simple operation: notify, update PCD, extract knowledge, create artifact |
| `sub_workflow` | Invokes another workflow definition |

### Task Statuses

`proposed` → `approved` → `queued` → `running` → `completed` | `failed` | `skipped` | `cancelled`

### Task Sources

Where the task came from: `workflow`, `planner`, `manual`, `ad_hoc`, `retry`, `system`

### Entity List

Design entities for ALL of the following. Group them logically.

**Project scope (the container):**
- Project (name, objective, description, brief, status, budget_ceiling, jurisdiction, template_name, tier)
- ProjectContext / PCD (versioned JSON, path-based mutations, format_version)
- ContextChange (audit trail for PCD mutations: path, old_value, new_value, agent, reason)
- ContextMilestone (named snapshot of PCD at a point in time)
- ProjectIntent (versioned objective understanding, revision chain via revised_from FK)
- ProjectAgent (junction: project ↔ agent_catalog, with role, user_prompt, enabled, display_order, custom_constraints)
- ProjectMember (junction: project ↔ user, with project role: owner/operator/reviewer/viewer)

**Task (the primitive):**
- Task (type, status, objective, agent_name, source, workflow_node_id, parent_task_id self-FK, dependencies, inputs JSON, outputs JSON, output_summary, cost_usd, duration_seconds, retry_count, max_retries, assigned_to user for human decisions, session_id for approval conversations, created_by, format_version)
- TaskAttempt (attempt_number, status: passed/failed, failure_tier, failure_reason, feedback_provided, input_tokens, output_tokens, cost_usd)
- TaskDependency (junction table: task_id, depends_on_task_id — for proper relational dependencies instead of JSON array)

**Knowledge (by-products of task execution):**
- ProjectLearning (kind, title, body, recommendation, confidence, occurrence_count, evidence JSON, agent_names, domain_tags, status: active/retracted/superseded, retracted_by, retracted_reason, superseded_by, contradicts_id, platform_promoted, embedding vector(1536), format_version)
- ProjectDecision (decision_id string, domain, decision, rationale, made_by, execution_id, status: active/superseded/reversed, superseded_by)
- MilestoneSummary (title, summary, execution_ids JSON, key_outcomes JSON, domain_tags, period_start, period_end)

**Artifacts (outputs of task execution):**
- ProjectArtifact (task_id FK, agent_name, artifact_type, title, content_format: markdown/pptx/docx/xlsx/pdf/code/json, content_text for inline, storage_url for binary, file_size_bytes, content_hash, language for code, metadata JSON, format_version)

**Documents (uploaded reference materials):**
- ProjectDocument (file_name, content_type MIME, file_size_bytes, storage_url, content_hash, extracted_text for small docs, extracted_text_url for large, page_count, extraction_status: pending/processing/completed/failed, extraction_error, document_type: reference/policy/data/report/code/other, description, tags, embeddings_generated, chunk_count, uploaded_by user FK)
- DocumentChunk (document_id FK, project_id denormalized, chunk_index, content text, embedding vector(1536), page_number, section_title)

**Sessions (transport):**
- Session (status: active/suspended/completed/expired/failed, user_id, project_id, agent_name, goal, rolling_summary, rolling_summary_anchor, rolling_summary_version, total_input_tokens, total_output_tokens, total_cost_usd, cost_budget_usd, last_activity_at, expires_at)
- SessionMessage (session_id FK, role: user/assistant/system/tool_call/tool_result, content text, sender_id, model, input_tokens, output_tokens, cost_usd, thinking text, tool_name, tool_call_id, status)
- SessionChannel (session_id FK, channel_type, channel_id, bound_at, is_active)

**Workflows:**
- WorkflowDefinition (project_id nullable for platform templates, name, description, version, enabled, nodes JSON, edges JSON, canvas_state JSON, designed_by, designed_by_agent, locked_at, format_version)
- WorkflowRun (workflow_name, workflow_version, project_id, status: pending/running/awaiting_input/completed/failed/cancelled, session_id, trigger_type, triggered_by, context JSON, total_cost_usd, budget_usd, error_data JSON, result_summary)
- WorkflowSchedule (workflow_definition_id FK, project_id FK, cron_expression, enabled, next_run_at, last_run_at)
- HumanInputRequest (workflow_run_id FK, task_id FK, project_id, session_id, prompt_message, channel, choices JSON, status: pending/completed/timed_out/cancelled, response, responder_id, timeout_at, resolved_at)

**Events (console/audit):**
- ProjectEvent (project_id FK, task_id FK nullable, session_id nullable, event_type, timestamp, source, data JSON, severity: info/warning/error)

**Platform entities (not scoped to project):**
- User (email unique, display_name, hashed_password nullable, system_role: platform_admin/member, is_active, is_service_account, last_login_at)
- ApiKey (user_id FK, name, key_prefix, hashed_key, expires_at, revoked_at, last_used_at, scopes JSON)
- AgentCatalog (agent_name unique, agent_type, version, description, system_prompt, model_config JSON, tools JSON, scope JSON, interface JSON, constraints JSON, keywords JSON, thinking_budget JSON, enabled, chat_enabled, visibility, engine, max_input_length, max_budget_usd, produces_artifact, artifact_type)
- PromptVersion (entity_type, entity_id, prompt_type, content, version, changed_by, change_reason)
- LlmCall (session_id, project_id, task_id, agent_name, agent_role, model, provider, input_tokens, output_tokens, cache_read_tokens, cache_creation_tokens, cost_usd, latency_ms, request_id, tool_count)
- ModelPricing (composite PK: model + effective_from, effective_to, price_per_mtok_input, price_per_mtok_output, price_per_mtok_cache_read, price_per_mtok_cache_create)
- Embedding (source_table, source_id, project_id FK, embedding vector(1536), model, version, content_hash)

### Technical Requirements

**PostgreSQL + EF Core specifics:**
- UUIDv7 primary keys (Npgsql 9.0+ generates client-side by default)
- `xmin` for optimistic concurrency (`[Timestamp] public uint Version`)
- `jsonb` columns via `[Column(TypeName = "jsonb")]` or `ToJson()` for complex types
- pgvector `vector(1536)` with HNSW cosine indexes for embeddings
- Time-partitioned `llm_calls` table (RANGE by month) with BRIN index on `created_at`
- Time-partitioned `project_events` table (RANGE by month)
- Cascading deletes: deleting a Project cascades through ALL project-scoped entities
- `format_version` string field on PCD, workflow definitions, tasks, learnings, artifacts

**Indexes to design:**
- Every FK column must be indexed
- Composite indexes for common query patterns (project_id + status, project_id + created_at, etc.)
- Unique constraints where needed (project name, agent_name, email, etc.)
- HNSW vector indexes on embedding columns

**EF Core conventions:**
- Base classes: `EntityBase` (Id UUID + CreatedAt + UpdatedAt), `ProjectScopedEntity` (adds ProjectId FK)
- Enum storage: use PostgreSQL enums via Npgsql for status fields
- JSON columns: use `ToJson()` complex type mapping for structured JSON, `[Column(TypeName = "jsonb")]` for flexible JSON

### Output Format

Produce the following sections:

**1. Entity Relationship Diagram (text/ASCII)**
Show all entities grouped by category with relationships (1:1, 1:N, N:M) and FK directions.

**2. Base classes**
`EntityBase`, `ProjectScopedEntity`, `TaskScopedEntity` — show the C# code.

**3. Entity classes**
For EVERY entity listed above, produce the full C# class with:
- Properties with types and attributes
- Navigation properties
- No business logic — pure data model

**4. Enumerations**
All enum types used across entities.

**5. DbContext**
The full `AgenticWorkforceDbContext` with:
- All DbSet properties
- `OnModelCreating` with: relationships, cascades, indexes, unique constraints, composite keys, JSON mappings, vector indexes, enum mappings, partition configuration
- Extension registration for pgvector, enums

**6. Indexes summary table**
A table listing every index: table, columns, type (btree/hnsw/brin/gin), unique, purpose.

**7. Partition strategy**
Which tables are partitioned, partition key, partition interval, retention policy.

**8. Migration strategy**
How to handle initial migration, format version upgrades, and the `format_version` migration pattern.

### Constraints

- Use `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 and `Pgvector.EntityFrameworkCore` 0.3.0
- Target .NET 9 / EF Core 9 (with notes on EF Core 10 migration path)
- All timestamps are `DateTimeOffset` (timezone-aware)
- All monetary values are `decimal` with `Numeric(12,6)` precision
- No business logic in entities — they are pure data containers
- Every relationship must have explicit cascade behaviour documented

Keep total response under 5000 words. Code is preferred over prose. Produce compilable C# — not pseudocode.

---

## After Research

Save claude.ai's response as: `docs/098-research/R15-response-database-schema.md`

Then we will review it and create the actual schema files in `src/AgenticWorkforce.Domain/Entities/` and `src/AgenticWorkforce.Infrastructure/Data/`.
