# Agentic Workforce Platform — Architecture Walkthrough

**Version:** 1.0
**Date:** 2026-05-10
**Classification:** Internal
**Companion:** [Solution Architecture](../002-architecture/001-solution-architecture.md) (formal spec)

This document explains the architecture in plain language, step by step — what we're using, what decisions we made, and why.

---

## The Big Picture

The Agentic Workforce Platform is an AI-first agent orchestration system that deploys teams of specialised AI agents ("digital workers") for regulated financial services operations at Investec. These agents plan, code, review, research, and make decisions. They need to run for hours, pause for human approval, track every cent they spend, and produce an audit trail that satisfies regulators for 7 years.

The Python prototype ("Mission Control") proved the concept. Now we're rebuilding it properly in C# on Microsoft's stack, deployed on Azure, following Investec's ICE engineering standards.

### Architecture at a Glance

```
                           ┌──────────────────────────────────────┐
                           │        Client Surfaces               │
                           │  React SPA  |  CLI  |  TUI  |  API  │
                           └──────────┬───────────────────────────┘
                                      │ HTTPS (Entra ID / API Key)
                           ┌──────────▼───────────────────────────┐
                           │      BFF API (ASP.NET Core)          │
                           │  REST Endpoints  |  SignalR Hub      │
                           │  Auth  |  Rate Limiting              │
                           └──────────┬───────────────────────────┘
                                      │ Redis (queue + pub/sub)
                           ┌──────────▼───────────────────────────┐
                           │      Worker (Background Service)     │
                           │  Durable Task Orchestrators          │
                           │  MAF Agent Execution                 │
                           │  Workflow Engine                     │
                           └───┬──────────┬───────────┬───────────┘
                               │          │           │
                    ┌──────────▼──┐ ┌─────▼─────┐ ┌──▼──────────────┐
                    │ PostgreSQL  │ │   Redis    │ │ Azure AI Foundry│
                    │ + pgvector  │ │  (cache,   │ │  Claude Sonnet  │
                    │ (34 entities│ │   events,  │ │  Claude Haiku   │
                    │  + vectors  │ │   SignalR  │ │  GPT-4o         │
                    │  + audit)   │ │   backpl.) │ │  Embeddings     │
                    └─────────────┘ └───────────┘ └─────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │  ACA Dynamic        │
                    │  Sessions           │
                    │  (Hyper-V sandbox   │
                    │   for code exec)    │
                    └─────────────────────┘
```

---

## Layer 1: The Agent Framework — MAF

**Decision:** Microsoft Agent Framework (MAF) 1.5.0
**ADR:** [003 — Agent Model Design](../002-architecture/ADR-003-agent-model-design.md)

MAF is the successor to both Semantic Kernel and AutoGen — Microsoft merged them into one framework in April 2026. It gives us:

- **`AIAgent`** — the base abstraction for any agent
- **`ChatClientAgent`** — the workhorse implementation that wraps any LLM provider
- **`IChatClient`** — the universal adapter from `Microsoft.Extensions.AI` that lets us talk to Claude, GPT, Ollama, anything
- **Three-layer middleware** — agent run, function/tool, and chat client levels
- **Sessions** — conversation state that serialises and deserialises
- **Tools** — any C# method becomes a tool via `AIFunctionFactory.Create()`
- **Workflows** — graph-based multi-agent orchestration

**Why MAF and not LangChain, CrewAI, or raw SDKs?** Because we're a .NET shop on Azure. MAF is first-party Microsoft, GA with support tickets, integrates with Aspire and Foundry, and the middleware pipeline is exactly what we need for cost tracking and audit. It's also MIT-licensed.

**Key constraint we hit:** `ChatClientAgent` is **sealed** — you can't subclass it. So instead of inheritance, we compose: a database-driven `AgentFactory` reads agent definitions from the catalog and constructs `ChatClientAgent` instances at runtime with the right provider, tools, prompts, and middleware. This is actually cleaner — agents differ in configuration, not behaviour.

**ADR:** [016 — Agent Design](../002-architecture/ADR-016-agent-design.md) (complete catalog schema, prompt assembly, tool scoping, file access, per-project customisation, lifecycle)

**Two critical MAF settings discovered during research (R19):**
- **`UseProvidedChatClientAsIs = true`** — we compose our own `IChatClient` pipeline. Without this, MAF wraps it again with a second `FunctionInvokingChatClient`, causing double tool charges and broken middleware ordering.
- **`RequirePerServiceCallChatHistoryPersistence = true`** — saves intermediate tool results between each service call inside a tool loop. Required for crash recovery and audit compliance.

**Agent design in a nutshell:** Each agent has a catalog entry in the database with its model, tools, file scope, constraints, and system prompt. The `AgentFactory` assembles a 5-layer prompt (organisation → category → agent → project brief → user prompt), resolves tools from an explicit manifest via a `ToolRegistry`, enforces file scope at the tool level, and constructs a `ChatClientAgent` with a shared `IChatClient` pipeline. Empty tool manifest = zero tools (Principle 14: Secure by Default). Every agent has bounded resources: budget ceiling, input length, tool call limit, timeout (Principle 19).

---

## Layer 2: The LLM Providers — Foundry Anthropic + Azure OpenAI

**Decision:** Claude via Azure AI Foundry (Anthropic), GPT/embeddings via Azure OpenAI
**ADR:** [002 — LLM Provider Strategy](../002-architecture/ADR-002-llm-provider-strategy.md)

We use Claude (Sonnet 4.6, Haiku 4.5) as the primary reasoning engine — it's better at coding, security review, and planning for our use cases. GPT handles embeddings and cost-sensitive triage.

**Why Foundry rather than direct Anthropic API?** Three reasons:

1. **MACC-eligible** — Azure billing, consolidated invoice, burns down existing Azure commitment
2. **Entra ID auth** — Managed Identity, no API keys to store
3. **Audit logging** — Foundry diagnostic logs flow to Log Analytics automatically

**What we gave up:** Foundry Anthropic is still "preview" — no Microsoft SLA (it's a partner model), and inference runs on Anthropic's infrastructure, not in an Azure region. For our workloads (code analysis, architecture review, planning) this is acceptable. If we ever need to process PII through Claude with hard data residency, we'd route those agents through AWS Bedrock EU instead — the architecture is designed so swapping provider is a config change, not a re-architecture.

**How it works in code:** Each provider is a keyed `IChatClient` in DI. The `AgentFactory` picks the right one based on the agent's `model_config` from the database. Budget middleware sits in the `IChatClient` pipeline so it sees every single LLM call, including each iteration of the tool-calling loop.

---

## Layer 3: Workflows — Editable Graphs with Decision Points

**Decision:** Stored workflow definitions as editable directed graphs, interpreted by Durable Task, with typed decision nodes
**ADRs:** [001 — Workflow Engine](../002-architecture/ADR-001-workflow-engine.md), [013 — Workflow Design](../002-architecture/ADR-013-workflow-design.md)

Workflows are **the way projects get repeatable work done**. They are stored, versioned, editable directed graphs — not hard-coded logic. A workflow can be created by a human via a visual graph editor, or by a Workflow Agent via chat, and executed without deploying code.

### The Runtime Engine

The Python prototype used Temporal, which requires a separate AKS cluster — too much overhead. We chose **Durable Task SDK + Azure Durable Task Scheduler** (GA, Microsoft-supported, embeds in ASP.NET Core). A generic `WorkflowInterpreter` reads any stored workflow definition and walks the graph, executing each node as a Durable Task activity.

### The Node Types

| Node Type | What It Does | Decision? |
|-----------|-------------|-----------|
| **Agent Task** | Runs a specific agent with an objective | No |
| **Human Decision** | Pauses, presents choices to a human, resumes on response | **Yes** |
| **AI Decision** | Runs an agent with constrained output schema — the agent answers a specific question | **Yes** |
| **Parallel Split/Join** | Fan out to multiple paths, wait for all to complete | No |
| **Sub-workflow** | Invokes another workflow definition | No |
| **Action** | Notify, update PCD, create artifact, set variable | No |

### Decision Points — The Key Concept

Decision points are **designed into the graph** — you can see them, audit them, and know exactly where a human or AI made a choice. This is different from agents freelancing; the decision point is explicit and its outputs are constrained.

```
┌───────┐    ┌──────────────┐    ┌───────────────┐    ┌──────────────┐
│ Start │───▶│ Agent Task   │───▶│ AI Decision   │───▶│ Human        │
│       │    │ "Run OWASP   │    │ "Classify     │    │ Decision     │
│       │    │  scan"       │    │  severity"    │    │ "Approve     │
└───────┘    └──────────────┘    └───┬───────┬───┘    │  plan?"      │
                                     │       │        └──┬─────┬─────┘
                              critical│  acceptable   approve reject
                                     ▼       ▼          ▼     ▼
                              ┌──────────┐ ┌─────┐ ┌─────┐┌──────┐
                              │"Remediate│ │ End │ │"Fix"││ End  │
                              │ now"     │ │ OK  │ │     ││Reject│
                              └──────────┘ └─────┘ └─────┘└──────┘
```

**Human Decision:** Publishes a `HumanInputRequest`, pauses via `WaitForExternalEvent`, resumes when the human responds. The response determines which outgoing edge to follow. Segregation of duties enforced — if the workflow was triggered by an operator, only a reviewer can approve.

**AI Decision:** Runs an agent with a **constrained output schema** — the agent must choose from the defined options (e.g., `critical_found | acceptable | needs_review`), it cannot invent new paths. The agent's reasoning is recorded alongside the decision for audit.

### Authoring

Workflows are authored in two ways:

1. **Visual graph editor** — React Flow canvas in the frontend. Drag nodes, connect edges, configure properties, validate, run.
2. **Workflow Agent** — a specialised agent that designs workflows via chat. It proposes a graph, the user refines it, and on approval it's saved as a WorkflowDefinition.

Both produce the same stored artifact: a `WorkflowDefinition` entity with nodes (JSON), edges (JSON), and canvas state (JSON) — versioned, lockable, and scoped to a project or the platform.

### Why This Matters

The ad-hoc `awp run "do something"` still exists for one-off tasks. But for repeatable processes — security assessments, onboarding, reconciliation — workflows are the way:

- **Visible** — you can see the graph and understand what will happen
- **Deterministic** — the steps are defined, not emergent
- **Auditable** — every decision point is a recorded event
- **Repeatable** — run the same workflow on different inputs
- **Editable** — change the process without changing code

---

## Layer 4: The Data Layer — PostgreSQL + pgvector

**Decision:** Single Azure PostgreSQL Flexible Server with pgvector
**ADR:** [004 — Data Layer](../002-architecture/ADR-004-data-layer.md)

We evaluated four options:

| Option | Verdict | Why |
|--------|---------|-----|
| PostgreSQL + pgvector (single DB) | **Selected** | Full FK support, EF Core maturity, pgvector for vectors, one compliance boundary |
| Azure SQL + Azure AI Search | Runner-up | Two products, DiskANN not confirmed in SA North, migration friction from PG |
| PostgreSQL + Cosmos DB split | Rejected | Over-engineered, breaks transactional integrity |
| Cosmos DB for PostgreSQL (Citus) | Eliminated | On Microsoft's retirement path |

**PostgreSQL won because:**

- 34 entities with complex FK relationships and cascading deletes — relational is non-negotiable
- EF Core + Npgsql is fully mature (JSON columns, UUIDv7, optimistic concurrency via `xmin`)
- pgvector 0.8.0 handles our 10k-100k embeddings with sub-50ms p95 via HNSW — Azure AI Search is overkill at this scale
- The LLM call audit table stays in the same DB as a time-partitioned table (RANGE by month) — scales to hundreds of millions of rows
- One database = one connection string = one backup story = one compliance boundary
- Available in SA North with 3 AZs, zone-redundant HA, geo-redundant backup

**Multi-region:** Cross-region read replica to UK South with virtual endpoints for DR/data residency.

---

## Layer 5: Real-time Events — SignalR + Redis + SSE

**Decision:** Self-hosted SignalR with Redis backplane, plus plain SSE for ephemeral streams
**ADR:** [005 — Real-time Events](../002-architecture/ADR-005-realtime-events.md)

The platform streams events to React SPA, CLI, TUI, and Telegram. The CLI can't use WebSocket — it needs SSE.

| Option | Verdict | Why |
|--------|---------|-----|
| Self-hosted SignalR + Redis | **Selected** | ~$16/month, identical features at our scale |
| Azure SignalR Service | Rejected at this scale | Free tier caps at ~15 connections; Standard at $49/month is overkill for 5-20 consumers |
| Azure Web PubSub | Eliminated | No SSE fallback — WebSocket only on client side |

**The two patterns we use:**

1. **SignalR groups** for persistent channels (`project:{id}`, `session:{id}`, `user:{id}`) — multiple listeners, long-lived
2. **Plain SSE** (`Results.ServerSentEvents`) for ephemeral per-run agent response streams — matches Microsoft's own AG-UI pattern

MAF workflow events (`ExecutorCompletedEvent`, `AgentResponseUpdateEvent`, etc.) are published from the Worker to Redis pub/sub, picked up by the BFF's SignalR hub, and fanned out to connected clients.

### The Project Console

Every project has a **console view** — a real-time, scrollable, filterable timeline of every action and event. This is the primary observability surface for operators and reviewers.

**Principle: log everything by default, blacklist noise.** Every event (agent actions, tool calls, LLM responses, decisions, errors, state transitions) is captured. Noisy events (streaming token chunks, keepalive pings, cost micro-updates) are excluded via a configurable blacklist — never a whitelist. **Errors are never blacklisted** — they always surface immediately.

**Dual persistence:**
- **JSONL file** (`var/logs/events.jsonl`) — fast, append-only, synchronous, survives DB outage
- **`ProjectEvent` table** (PostgreSQL) — structured, queryable, paginated API, async write

Both are written for every event. The JSONL file is the safety net; the database is the queryable store. The console view in the React frontend renders events in real-time via the SignalR `project:{id}` channel, with client-side filtering by event type, agent, severity.

---

## Layer 6: Code Execution Sandbox — ACA Dynamic Sessions (Container-First)

**Decision:** Container-first execution — all tools that touch the network or filesystem run in ACA Dynamic Sessions (Hyper-V isolation). Only internal platform query tools are exempt.
**ADR:** [006 — Container Isolation](../002-architecture/ADR-006-container-isolation.md)
**Principle:** [22 — Container-First Tool Execution](../003-principles/001-architectural-principles.md)

Agents execute code — shell commands, git operations, builds, tests, static analysis. This must be sandboxed so one project can't affect another or the host.

**Why Dynamic Sessions?**

- **Hyper-V isolation** per session — strongest isolation on ACA
- **Sub-second startup** from pre-warmed pool
- **Network egress disabled by default** — enable only with Azure Firewall allowlist
- **Per-project session persistence** — same HMAC-derived identifier routes all tool calls to the same warm session
- **24-hour max session duration**

We build a hardened base image with git, dotnet SDK, Node, Python, security scanners — push it to ACR, pull with Managed Identity. Agents call tools (`RunShellAsync`, `WriteFileAsync`, `GitCloneAsync`) that delegate to the session via REST API.

| Option | Verdict | Why |
|--------|---------|-----|
| ACA Dynamic Sessions (custom container) | **Selected** | Hyper-V isolation, sub-second startup, managed |
| MAF/Foundry Code Interpreter | Rejected | Python-only, no shell/git/build tools, 1-hour max |
| Docker-in-Docker on ACA | Eliminated | ACA blocks privileged containers platform-wide |
| ACI | Fallback | For projects >24 hours or needing durable Azure Files workspace |
| AKS Kata pods | Deferred | Strongest isolation but highest operational complexity |

---

## Layer 7: Authentication — Entra ID + API Keys

**Decision:** Dual auth scheme — Entra ID JWT + API Key, both accepted on the same `[Authorize]`
**ADR:** [007 — Identity and Auth](../002-architecture/ADR-007-identity-auth.md)

**Why Entra ID?** It's the ICE standard. Avalanche scaffolds it. The bank already has a tenant. SSO for employees.

**Why keep API Keys?** Programmatic consumers (CI/CD, external systems) can't always do OAuth. Keys are hashed with HMAC-SHA256 + per-key salt, prefixed for identification, scoped to specific operations.

**How they compose:** A `PolicyScheme` with `ForwardDefaultSelector` inspects the request — if `X-Api-Key` header is present, forward to the API Key handler; otherwise forward to Entra ID JWT Bearer.

**Two-dimensional role model** separates platform governance from project participation:

**Platform Roles** (Entra ID App Roles — global):

| Role | What They Do |
|------|-------------|
| `platform_admin` | Manages the platform: agent catalog, templates, model governance, users, config, emergency stop, cross-project audit |
| `member` | Baseline authenticated user. Can be assigned to projects. |

**Project Roles** (per-project, stored in `project_members` table):

| Role | Can Run | Can Approve | Can Manage | Can View |
|------|---------|-------------|------------|----------|
| `owner` | Yes | Yes | Yes (team, budget, delete) | Yes |
| `operator` | Yes | Yes | No | Yes |
| `reviewer` | **No** | **Yes** | No | Yes |
| `viewer` | No | No | No | Yes |

**Segregation of duties** — critical for Maker-Checker patterns in a bank. The **Reviewer** role exists specifically so the person who approves is not the person who triggered the execution. The platform enforces: `triggered_by != approved_by` on all approval gates.

A single user can be Owner on one project, Reviewer on another, and Viewer on a third. Platform Admin can access all projects for audit/emergency but doesn't automatically get project roles.

**Zero secrets in infrastructure:** All Azure service auth via User-Assigned Managed Identity — Foundry, Key Vault, Storage, ACR, Dynamic Sessions. No API keys stored in config.

---

## Layer 8: Audit and Compliance — WORM + Hash Chain + KQL

**Decision:** Three-layer audit pipeline — MAF middleware, Event Hubs, Blob WORM + Fabric Eventhouse
**ADR:** [008 — Audit and Compliance](../002-architecture/ADR-008-audit-compliance.md)

This is non-negotiable for a dual-regulated bank (FCA/PRA in UK, SARB/PA in South Africa). Every LLM call, every tool invocation, every human approval must be recorded immutably for 7 years.

**The critical finding:** No built-in Azure AI audit trail exists. Neither Foundry, Purview, nor Agent 365 Observability provides an immutable per-conversation evidence record. We must build it.

**How it works:**

```
Agent execution → IChatClient middleware (non-blocking Channel<T>)
                        │
                        ▼
                  AuditDrainService (batches 100 records / 1s)
                        │
              ┌─────────┼─────────┐
              ▼                   ▼
       Evidence Store        Analytics Store
       (Blob WORM)          (Event Hub → Eventhouse)
       7-year locked         KQL queryable
       SHA-256 metadata      7-year retention
       Per-region ZRS        Daily Merkle root
```

1. **Capture:** `AuditingChatClient` (IChatClient middleware) captures every LLM call — tokens, model, latency, SHA-256 hashes of input/output. Uses `Channel<AuditRecord>` with backpressure (`Wait` mode, 5s timeout) — if the audit pipeline is saturated, agent execution halts rather than silently dropping records (Principle 8: Fail Fast).
2. **Transport:** Background `AuditDrainService` batches records and sends to Event Hubs (1 TU per region, ~$22/month).
3. **Evidence:** Full prompt/response JSON written to Azure Blob Storage with version-level WORM (7-year locked retention, Cohasset-attested for SEC 17a-4(f)).
4. **Analytics:** Event Hubs to Fabric Eventhouse (KQL) for compliance search, dashboards, anomaly detection. 7-year retention via `SoftDeletePeriod`.
5. **Tamper detection:** Per-stream SHA-256 hash chain with sequence numbers. Chain state (`seq` + `prevHash`) is persisted in Redis via atomic Lua scripts — survives pod restarts without breaking chain continuity. Daily Merkle root anchored to a WORM blob.

**Data residency:** Separate storage accounts + Event Hub namespaces per region (SA North, UK South). ZRS only — no GRS to avoid cross-jurisdiction replication.

**Cost:** ~$600/month for both regions (~$0.83 per 1,000 audited turns).

---

## Layer 9: Cost Tracking — DelegatingChatClient Middleware

**Decision:** Budget enforcement as IChatClient middleware, not agent-level
**ADR:** [009 — Cost Tracking](../002-architecture/ADR-009-cost-tracking.md)

**Why IChatClient and not AIAgent middleware?** Because the `FunctionInvokingChatClient` re-enters `GetResponseAsync` once per tool round-trip. Middleware inside the tool loop sees **every individual model call** — which is what you need for accurate per-call cost recording. The agent-level `AgentRunResponse.Usage` is a framework roll-up that may understate spend.

**The budget hierarchy:**

| Level | Enforcement |
|-------|-------------|
| Per-call | Record cost |
| Per-agent per-execution | Hard stop at $1.00 ceiling (configurable) |
| Per-execution | Hard stop |
| Per-session | Warning at 80%, hard stop at 100% ($50 default) |
| Per-project | Warning at 80%, hard stop at 100% |
| Per-hour platform-wide | Alert only ($5/hr) |

**No model downgrade. Ever.** When budget is exceeded, the execution fails immediately with a clear error. A wrong answer from a cheaper model is worse than a stopped execution. Accuracy and predictability are paramount. Budget warnings at 80% notify users via SignalR so they can proactively extend the budget before the hard stop fires.

---

## Layer 10: Context Assembly — AIContextProvider + Token Budget Trimming

**Decision:** Custom `AIContextProvider` for per-turn context injection with priority-based token budget management
**ADR:** [010 — Context Assembly](../002-architecture/ADR-010-context-assembly.md)

This is the system's brain — it determines what each agent knows before executing a task. Quality of context = quality of agent output.

**How it works:** Before every agent call, the `ProjectContextProvider` assembles a multi-layer context packet:

| Priority | Layer | Trim Behaviour |
|----------|-------|---------------|
| 0 (highest) | Project Context Document (PCD) | NEVER trimmed |
| 1a | Task definition | NEVER trimmed |
| 1b | Upstream task inputs | Summarised if over budget |
| 2.5 | Learnings from previous runs | Skipped if budget is tight |
| 3 | Code Map (repo structure) | Only for coding tasks, token-capped |
| 2 (lowest) | Execution history | Trimmed FIRST |

The `ContextAssembler` fills layers in priority order, token-counts each, and trims from the bottom when the budget is exceeded.

**Token counting:** `Microsoft.ML.Tokenizers` for GPT models (local, synchronous). For Claude, there is **no local tokenizer** — we must call Anthropic's `count_tokens` API (free but rate-limited). Tiktoken is NOT accurate for Claude — Opus 4.7 can produce up to 1.47x more tokens on the same input.

**Prompt layering:** 5 layers assembled into a single `Instructions` string:

| Order | Layer | Source |
|-------|-------|--------|
| 1 | Organization prompt | Disk file (global) |
| 2 | Category prompt | Disk file (per agent type) |
| 3 | Agent system prompt | Database (versioned) |
| 4 | Project brief | Database (per project) |
| 5 | User prompt | Database (per agent per project) |

**Session memory compression:** When message count exceeds threshold (50), a Haiku summarizer agent produces a rolling summary. The summary replaces old messages; the anchor moves forward.

### Knowledge Accumulation — The System Gets Smarter

Context assembly is only the read side. The write side is equally important — after every agent task, a `KnowledgeExtractor` (lightweight Haiku agent, ~$0.001 per call) analyses the output and extracts reusable learnings.

**ADR:** [014 — Knowledge and Memory Management](../002-architecture/ADR-014-knowledge-memory.md)

**The read-write cycle:**

```
BEFORE each task:                          AFTER each task:
├── PCD (principles, guardrails)           ├── Extract learnings from output
├── Platform learnings (proven patterns)   ├── Deduplicate via vector similarity
├── Project learnings (by confidence)      │   (>0.92 cosine = same → reinforce)
├── Active decisions                       ├── Store new discoveries (confidence 0.5)
├── Existing findings (research: dedup)    ├── Update PCD (current_state, architecture)
├── Document chunks (semantic search)      └── Record decisions with rationale
├── Code Map (coding tasks)
└── Execution history (trimmed first)
```

**Human direction vs agent discovery:** When a human says "always use async/await" → it goes into PCD `principles` (absolute authority, never trimmed). When an agent discovers "async/await reduced errors by 30%" → it becomes a `ProjectLearning` (advisory, retractable, starts at 0.5 confidence, grows with confirmation).

**Deduplication matters.** A daily AI news research project would learn the same stories 50 times without deduplication. Every new learning is embedded and compared against existing learnings — duplicates reinforce confidence rather than creating redundant entries. Research projects also get an explicit "do not repeat these existing findings" instruction in their context.

**Human control:** Owners and Operators can retract wrong learnings (marked as wrong, excluded from context, audit trail preserved — never truly deleted). Owners can edit or supersede with corrections. Proven learnings (confidence ≥ 0.7, confirmed across ≥ 2 projects) can be promoted to platform-wide knowledge by Platform Admins.

**The Knowledge View** in the UI shows all learnings with confidence scores, occurrence counts, agent attribution, evidence links, and retraction status. Humans can see exactly what the platform has learned, correct mistakes, and add direction.

---

## Layer 11: Tool Integration — Container-First Execution

**Decision:** Container-first: all tools that touch the network or filesystem run in ACA Dynamic Sessions or MCP containers. Only internal platform query tools run in-process.
**ADRs:** [006 — Container Isolation](../002-architecture/ADR-006-container-isolation.md), [011 — MCP and A2A](../002-architecture/ADR-011-mcp-a2a.md)
**Principle:** [22 — Container-First Tool Execution](../003-principles/001-architectural-principles.md)

### Why Container-First?

The BFA prototype ran all tools `local` by default, relying on `FileScope` (software-level checks) for isolation. This is insufficient for a dual-regulated bank:

1. A bug in FileScope or a missing check in a new tool bypasses isolation entirely. The container boundary is OS-level — it cannot be bypassed by application bugs.
2. Tools making HTTP calls in-process (web search, SonarQube) have the Worker's unrestricted network access. Containerized, they go through the Azure Firewall egress allowlist.
3. Prompt injection steering a `web.search` tool argument can exfiltrate data via arbitrary HTTP if the tool runs in-process.

### Two Execution Domains

| Domain | Where | Overhead | What |
|--------|-------|----------|------|
| **Platform** (in-process) | Worker process | Zero | Internal DB queries: `project.get_info`, `project.get_pcd`, `project.get_team`, `project.approve_tasks`, etc. No network, no filesystem. |
| **Sandbox** (containerized) | ACA Dynamic Sessions (Hyper-V) or MCP container | ~10ms | **Everything else.** File ops, shell, git, web search, Azure SDK calls, security scanners, external API calls. |

### Concrete Tool Mappings

| Target | Execution Domain | Pattern |
|--------|-----------------|---------|
| `project.*` tools (get_info, get_pcd, get_team, approve_tasks, etc.) | **Platform** (in-process) | Native `AIFunction` calling service interfaces |
| Web search (Tavily, Brave, Perplexity) | **Sandbox** (Dynamic Sessions) | `AIFunction` delegating to `IDynamicSessionClient` with egress allowlist |
| SonarQube, Snyk, compliance API | **Sandbox** (Dynamic Sessions) | `AIFunction` delegating to `IDynamicSessionClient` with egress allowlist |
| Azure AI Search, SharePoint/Graph | **Sandbox** (Dynamic Sessions) | `AIFunction` delegating to `IDynamicSessionClient` with Entra ID auth forwarded |
| File read/write/search, shell execute | **Sandbox** (Dynamic Sessions) | `AIFunction` delegating to `IDynamicSessionClient` |
| Bandit, Semgrep (security scanners) | **Sandbox** (MCP container) | MCP server in container, JSON-RPC |
| Git operations | **Sandbox** (Dynamic Sessions or MCP) | `AIFunction` or MCP, always containerized |
| External compliance agent (other team) | **Remote** | A2A proxy (`A2AAgent`) |

**New tools default to Sandbox.** A tool registered without specifying execution domain defaults to containerized. Platform domain requires explicit justification and an architecture test.

**Web search:** Custom failover chain (Tavily then Brave then Perplexity) running inside Dynamic Sessions. The egress allowlist explicitly permits `api.tavily.com`, `api.search.brave.com`, `api.perplexity.ai`. All other egress is denied.

**A2A maturity:** The protocol (v1) is stable and Linux Foundation governed. MAF packages are still preview — we pin versions and isolate behind a thin abstraction.

---

## Layer 12: How It All Deploys — Aspire + Avalanche

**Decision:** Separate Api + Worker Container Apps, Vertical Slice architecture, two-layer Bicep
**ADR:** [012 — Aspire Integration](../002-architecture/ADR-012-aspire-integration.md)

### Two Container Apps, not one

| Concern | BFF (Api) | Worker |
|---------|-----------|--------|
| Ingress | External HTTP | None (internal only) |
| Scaling | HTTP concurrency | KEDA (Redis queue length) |
| Responsibilities | REST API, SignalR hub, auth, short ops | Workflows, agent execution, DurableTask host |
| Identity | Own UAMI (API-facing RBAC) | Own UAMI (AI Foundry, DB, sandbox RBAC) |

Separating them means long-running agent executions don't block API responsiveness, they scale independently, and they have separate Managed Identities for least-privilege RBAC.

**Communication:** Api enqueues work to Redis Stream, Worker picks up via `XREADGROUP`, runs the workflow, publishes events via Redis pub/sub, Api's SignalR hub fans out to clients.

### Two-layer Bicep

| Layer | Source | What It Provisions |
|-------|--------|-------------------|
| Platform (Avalanche) | Avalanche scaffold | VNet, NSGs, DNS, Key Vault, Log Analytics, App Insights, ACA Environment |
| Application (Aspire) | `azd infra synth` | Container Apps, ACR, identities, role assignments, probes |

They compose via well-known parameter outputs: Avalanche outputs `managedEnvironmentId`, `KeyVaultName`, `LogAnalyticsWorkspaceId`; Aspire consumes them. `RunAsExisting` / `PublishAsExisting` binds to Avalanche-provisioned resources without duplication.

### Vertical Slice over Clean Architecture

Each command/query in the API owns its handler, DTOs, and persistence. This avoids the over-abstracted layer cake that hurts iteration speed when prompts and agent logic change weekly. A small shared `Domain` library holds cross-cutting primitives (entities, enums, interfaces).

---

## Artifacts vs Agent Outputs

There are two distinct types of output, and they serve different audiences.

**Artifacts** are the **deliverables** — the polished outputs that stakeholders care about. The security report. The onboarding file. The remediation plan. These are the reason the project exists. They persist with the project forever and are viewable in an **Artifact Gallery** in the UI.

**Agent outputs** are the **raw task results** — what each agent produced during each execution. The scanner's JSON findings. The planner's task DAG. The verifier's pass/fail assessment. These are operational — visible in the **Execution Detail** view for operators and developers.

### Artifact Formats

Agents produce structured content (markdown, data tables). Function tools convert to polished formats:

| Format | How It's Generated | Storage | UI |
|--------|--------------------|---------|-----|
| Markdown reports | Agent writes directly | Inline in DB | Rendered as HTML with Mermaid diagrams |
| PowerPoint (PPTX) | Agent writes markdown → `GeneratePresentation` tool converts | Azure Blob Storage | Download + preview |
| Word (DOCX) | Agent writes markdown → `GenerateDocument` tool converts | Azure Blob Storage | Download + preview |
| Excel (XLSX) | Agent produces data → `GenerateSpreadsheet` tool converts | Azure Blob Storage | Download + preview |
| PDF | Agent writes content → `GeneratePdf` tool converts | Azure Blob Storage | Inline PDF viewer |
| Code files | Agent writes code directly | Inline in DB | Syntax-highlighted viewer |

The agent decides **what** to produce. The document generation tools handle **how** to format it. Libraries: `DocumentFormat.OpenXml` (DOCX/PPTX), `ClosedXML` (XLSX), `QuestPDF` (PDF), `Markdig` (markdown rendering).

### The Two UI Views

**Artifact Gallery** (per project) — all deliverables in one place:
- Markdown artifacts rendered inline as formatted HTML
- Office documents downloadable with thumbnails
- Filterable by type, agent, date
- This is what stakeholders, reviewers, and auditors look at

**Execution Detail** — raw operational data per execution:
- JSON tree viewer for structured agent output
- Diff viewer for code changes
- Pass/fail badges for verification outcomes
- Links to any artifacts the execution produced
- This is what operators use for debugging and understanding what happened

### Document Uploads

Users often need to upload reference materials for agents to analyse — policy documents, standards, data files, previous reports, spreadsheets. These are stored in **Azure Blob Storage** (not the database) with metadata in PostgreSQL.

**Why Blob Storage, not the DB?** The database stays lean. A project with 500 uploaded PDFs doesn't bloat PostgreSQL, slow backups, or create vacuum pain. ACA pods are ephemeral, so local disk isn't an option either. Blob Storage is purpose-built, shared across replicas, and we already have it for artifacts and audit.

**The pipeline:** Upload is instant (raw file → Blob, metadata → DB). Text extraction runs in the background (Worker picks it up):

1. `PdfPig` extracts text from PDFs, `DocumentFormat.OpenXml` handles DOCX/PPTX, `ClosedXML` handles XLSX
2. Extracted text is chunked (~500 tokens per chunk) and embedded in pgvector
3. The document becomes searchable — agents can find relevant sections via semantic similarity

**How agents use uploaded documents:**
- **Small, always-relevant docs** (policy, project brief) → injected directly into agent context via `ProjectContextProvider`
- **Large docs or many docs** → agent calls a `SearchDocuments` tool that queries pgvector embeddings for the most relevant chunks
- **Specific document** → agent calls `ReadDocument` by name to get the full extracted text

The raw file is always preserved in Blob Storage for download. The extracted text is what agents work with. Scanned PDFs can optionally use Azure AI Document Intelligence for OCR.

---

## The End-to-End Flow

A user says "Scan the payments module for OWASP Top 10 vulnerabilities":

1. **React SPA** sends the request to the **BFF API** (authenticated via Entra ID)
2. **BFF** creates an execution record in PostgreSQL and enqueues to **Redis Stream**
3. **Worker** picks up the job and starts a **Durable Task orchestrator**
4. Orchestrator calls the **Planner agent** (Claude Sonnet via Foundry) — which reads the PCD, code map, and past learnings via the `AIContextProvider`, then produces a task DAG
5. Orchestrator pauses with `WaitForExternalEvent<Approval>("plan_approval", 4h)` — user gets a SignalR notification
6. User approves in the UI — Durable Task resumes
7. Orchestrator dispatches tasks to **Security Reviewer agent** — which calls MCP tools (Semgrep in a container) and native function tools (file read, web search) within a **Hyper-V isolated Dynamic Session**
8. Every LLM call passes through `BudgetEnforcingChatClient` (cost check) and `AuditingChatClient` (non-blocking audit record via Channel, Event Hub, WORM Blob)
9. Verifier agent checks the findings; retry decisions loop back if quality is insufficient
10. Results are written as `ProjectArtifact`s, PCD is updated, learnings are extracted
11. Events stream to the React SPA in real-time via SignalR

The whole thing survives pod restarts, tracks every cent, and produces a 7-year compliance trail.

---

## Key Risks

| Risk | Impact | Mitigation |
|------|--------|-----------|
| MAF sub-packages still preview (Anthropic, DurableTask, A2A) | API surface may shift | Pin versions in `Directory.Packages.props`; isolate behind thin abstractions |
| Aspire has no LTS | Only latest release supported | Plan minor-version upgrades every 6 months |
| Foundry Anthropic is preview; inference on Anthropic infra | Data residency for PII workloads | Architecture supports provider swap as config change; evaluate Bedrock EU if needed |
| No local Claude tokenizer | Token budget estimation requires API call | Cache aggressively; calibrate offline linear estimator as fallback |
| No built-in Azure AI audit trail | Must build entire evidence subsystem | Custom audit pipeline with WORM + hash chain (ADR-008) |
| Two Bicep layers must compose | Duplicate resource errors if not coordinated | `RunAsExisting` pattern; parameter contracts as interface |

---

## Reference Documents

| Document | Location |
|----------|----------|
| Solution Architecture (formal) | [002-architecture/001-solution-architecture.md](../002-architecture/001-solution-architecture.md) |
| Architecture Decision Records (17) | [002-architecture/ADR-001 through ADR-017](../002-architecture/) |
| Research Prompts and Responses (12) | [098-research/](../098-research/) |
| Requirements Specification | [096-requirements/001-mission-control-requirements.md](../096-requirements/001-mission-control-requirements.md) |
| Agentic Workforce BRD | [096-requirements/003-agentic-ai-workforce-brd.md](../096-requirements/003-agentic-ai-workforce-brd.md) |
| Agentic Workforce TRD | [096-requirements/003-agentic-ai-workforce-trd.md](../096-requirements/003-agentic-ai-workforce-trd.md) |
| Avalanche BFF Skeleton Analysis | [098-research/002-avalanche-bff-skeleton-analysis.md](../098-research/002-avalanche-bff-skeleton-analysis.md) |
