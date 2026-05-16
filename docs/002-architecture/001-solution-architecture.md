# Solution Architecture: Agentic Workforce Platform

**Version:** 1.0
**Date:** 2026-05-10
**Classification:** Internal — Confidential
**Status:** Approved (Architecture Decision Records signed off)

---

## 1. Executive Summary

The Agentic Workforce Platform is an AI-first agent orchestration system that deploys, manages, and governs teams of specialised AI agents ("digital workers") for regulated financial services operations at Investec. It is built in **C# on the Microsoft Agent Framework (MAF)**, deployed on **Azure Container Apps** via Investec's Avalanche scaffold, and uses **Azure AI Foundry** as a multi-model gateway for Claude and GPT inference.

The platform replaces a Python prototype ("Mission Control") with a production-grade, enterprise architecture aligned to ICE standards, Entra ID identity, and the bank's dual-regulatory obligations (FCA/PRA in the UK, SARB/PA in South Africa).

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

## 2. Technology Stack

| Layer | Technology | Version | ADR |
|-------|-----------|---------|-----|
| **Agent Framework** | Microsoft Agent Framework (MAF) | 1.5.0 (GA) | [003](ADR-003-agent-model-design.md) |
| **AI Abstractions** | Microsoft.Extensions.AI | 10.5.x (GA) | [003](ADR-003-agent-model-design.md) |
| **Orchestration** | .NET Aspire | 13.3 | [012](ADR-012-aspire-integration.md) |
| **Workflow Durability** | Durable Task SDK + Azure Durable Task Scheduler | 1.19.0 (GA) | [001](ADR-001-workflow-engine.md) |
| **Primary LLM** | Claude Sonnet 4.6 / Haiku 4.5 via Foundry Anthropic | — | [002](ADR-002-llm-provider-strategy.md) |
| **Embeddings** | text-embedding-3-small via Azure OpenAI | — | [002](ADR-002-llm-provider-strategy.md) |
| **Database** | Azure PostgreSQL Flexible Server + pgvector 0.8.0 | PG 16/17 | [004](ADR-004-data-layer.md) |
| **Cache / Events** | Azure Cache for Redis | — | [005](ADR-005-realtime-events.md) |
| **Real-time** | Self-hosted SignalR + Redis backplane | — | [005](ADR-005-realtime-events.md) |
| **Code Sandbox** | ACA Dynamic Sessions (Hyper-V, custom container) | — | [006](ADR-006-container-isolation.md) |
| **Auth** | Entra ID (Microsoft.Identity.Web) + API Key dual scheme | 4.9.x | [007](ADR-007-identity-auth.md) |
| **Audit** | Event Hubs + Blob WORM + Fabric Eventhouse | — | [008](ADR-008-audit-compliance.md) |
| **Hosting** | Azure Container Apps | — | [012](ADR-012-aspire-integration.md) |
| **IaC** | Bicep (Avalanche platform + Aspire app) | — | [012](ADR-012-aspire-integration.md) |
| **CI/CD** | Azure DevOps (3-pipeline pattern from Avalanche) | — | [012](ADR-012-aspire-integration.md) |
| **Frontend** | React + Vite + Tailwind | — | — |

---

## 3. Deployment Topology

### 3.1 Azure Resources (South Africa North)

```
┌─── Azure Container Apps Environment (Internal, Private Endpoint) ──────┐
│                                                                         │
│  container-app: agentic-workforce-api       (external HTTP, sticky)    │
│  container-app: agentic-workforce-worker    (internal only, KEDA)      │
│  container-app: agentic-workforce-frontend  (external HTTP, static)    │
│  session-pool:  agentic-workforce-sandbox   (Dynamic Sessions, Hyper-V)│
└─────────────────────────────────────────────────────────────────────────┘

┌─── Data ────────────────────────────────────────────────────────────────┐
│  Azure Database for PostgreSQL Flexible Server                         │
│  ├── Zone-redundant HA (3 AZs), GP_Standard_D4ds_v5                   │
│  ├── Extensions: vector, pg_diskann, pgaudit, pg_partman, uuid-ossp   │
│  ├── CMK in Key Vault, Private Endpoint                               │
│  └── Cross-region read replica → UK South (DR)                        │
│                                                                         │
│  Azure Cache for Redis                                                 │
│  └── Events, SignalR backplane, sessions, idempotency cache            │
└─────────────────────────────────────────────────────────────────────────┘

┌─── AI ──────────────────────────────────────────────────────────────────┐
│  Azure AI Foundry Project (single resource per environment)            │
│  ├── Anthropic: Claude Sonnet 4.6, Haiku 4.5, Opus 4.6               │
│  └── Azure OpenAI: GPT-4o, text-embedding-3-small                    │
└─────────────────────────────────────────────────────────────────────────┘

┌─── Durability ──────────────────────────────────────────────────────────┐
│  Azure Durable Task Scheduler (Consumption SKU)                        │
└─────────────────────────────────────────────────────────────────────────┘

┌─── Security ────────────────────────────────────────────────────────────┐
│  Key Vault (secrets via UAMI, no stored credentials)                   │
│  Entra ID App Registration (4 app roles: viewer/user/admin/sysadmin)   │
│  User-Assigned Managed Identity per Container App                      │
└─────────────────────────────────────────────────────────────────────────┘

┌─── Observability ───────────────────────────────────────────────────────┐
│  Application Insights (OTel sink from MAF GenAI semantic conventions)  │
│  Log Analytics Workspace (audit, pgAudit, session logs)                │
└─────────────────────────────────────────────────────────────────────────┘

┌─── Audit ───────────────────────────────────────────────────────────────┐
│  Azure Blob Storage (WORM, version-level, ZRS, 7-year locked)         │
│  Event Hubs Standard (1 TU, regional namespace)                        │
│  Microsoft Fabric Eventhouse (KQL analytics, 7-year retention)         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Multi-Region (Data Residency)

| Region | Role | Resources |
|--------|------|-----------|
| **South Africa North** | Primary | All compute, data, AI, audit |
| **UK South** | DR + UK data residency | PostgreSQL read replica, Blob WORM (UK), Event Hub namespace (UK) |

No cross-region replication for audit stores. Each region is a complete, independent compliance boundary.

---

## 4. Solution Structure

```
AgenticWorkforce/
├── .azuredevops/                              # Avalanche: CI/CD pipelines
│   ├── pipelines/
│   │   ├── azure-deploy.yml                   # Build → validate → deploy (dev/prod with gates)
│   │   ├── azure-cleanup.yml                  # Tear down temporary resources
│   │   └── azure-pr-validation.yml            # Lint, test, Bicep validation on PRs
│   └── templates/
│       ├── stages/
│       └── variables/ (global.yml, dev.yml, prod.yml)
│
├── infra/
│   ├── platform/                              # Avalanche: VNet, NSGs, DNS, KV, Log Analytics, ACA Env
│   └── app/                                   # Aspire-generated: Container Apps, identities, roles
│
├── src/
│   ├── AgenticWorkforce.sln
│   ├── AgenticWorkforce.AppHost/              # Aspire orchestrator
│   ├── AgenticWorkforce.ServiceDefaults/      # Health, OTel, resilience, service discovery
│   ├── AgenticWorkforce.Api/                  # BFF (HTTP ingress, SignalR, auth)
│   ├── AgenticWorkforce.Worker/               # Background (workflows, agent execution)
│   ├── AgenticWorkforce.Agents/               # MAF agents, tools, middleware, context providers
│   ├── AgenticWorkforce.Domain/               # Entities, enums, interfaces
│   ├── AgenticWorkforce.Infrastructure/       # EF Core, Redis, external integrations
│   └── tests/
│       ├── AgenticWorkforce.Tests.Unit/
│       ├── AgenticWorkforce.Tests.Integration/
│       └── AgenticWorkforce.Tests.Architecture/
│
├── frontend/                                  # React + Vite + Tailwind SPA
├── azure.yaml                                 # azd configuration
└── README.md
```

### Project Dependency Graph

```
AppHost → references all projects (orchestration only)

Api ──────→ Agents ──→ Domain
  │              │
  └──→ Infrastructure ──→ Domain

Worker ───→ Agents ──→ Domain
  │              │
  └──→ Infrastructure ──→ Domain

ServiceDefaults → referenced by Api + Worker
```

---

## 5. Component Architecture

### 5.1 BFF API (`AgenticWorkforce.Api`)

The HTTP-facing surface. Handles auth, routing, real-time events, and short-lived operations.

| Responsibility | Implementation |
|---------------|----------------|
| REST API (~80 endpoints) | ASP.NET Core Minimal API / Controllers, vertical slices |
| Real-time events | SignalR hub (`ProjectHub`) with Redis backplane |
| Ephemeral streams | Plain SSE via `Results.ServerSentEvents` |
| Authentication | Entra ID JWT + API Key dual scheme via `AddPolicyScheme` |
| Authorization | 4 app roles (hierarchical) + per-project membership handler |
| Work dispatch | Enqueues to Redis Stream → Worker picks up |

### 5.2 Worker (`AgenticWorkforce.Worker`)

Background service with no HTTP ingress. Runs long-lived workflows and agent executions.

| Responsibility | Implementation |
|---------------|----------------|
| Project lifecycle | Durable Task SDK orchestrators (plan → gate → dispatch → execute → verify) |
| Agent execution | MAF `ChatClientAgent` inside Durable Task activities |
| Agent graph | MAF `WorkflowBuilder` (executors + edges) invoked within activities |
| Human-in-the-loop | `WaitForExternalEvent<T>(timeout)` for approval gates |
| Scheduled jobs | DTS scheduling (preview) or ACA cron jobs |
| Event publishing | Redis pub/sub → BFF SignalR hub fans out to clients |
| Scaling | KEDA scaler on Redis queue length |

### 5.3 Agents (`AgenticWorkforce.Agents`)

Shared library defining all agent types, tools, middleware, and context assembly.

| Component | Pattern |
|-----------|---------|
| Agent catalog | Database-driven `AgentFactory` → `ChatClientAgent` per agent definition |
| Prompt assembly | 5-layer composition → single `Instructions` string |
| Context injection | Custom `AIContextProvider` (PCD, task, learnings, code map, history) |
| Token budgets | `ContextAssembler` with priority-based trimming |
| Session memory | Rolling summary compression via Haiku summarizer agent |
| Tools | `AIFunctionFactory.Create()` for Azure SDK tools; MCP for sandboxed scanners |
| Cost tracking | `BudgetEnforcingChatClient : DelegatingChatClient` |
| Audit | `AuditingChatClient : DelegatingChatClient` |
| Content safety | IChatClient middleware calling Azure AI Content Safety |
| Template inheritance | `TemplateResolver` (base → category → concrete, monotonic guardrails) |

### 5.4 Domain (`AgenticWorkforce.Domain`)

34 entities ported from the requirements specification:

**Primitive:** Task. **Scope:** Project. **Transport:** Session. (See [Architectural Principles](../003-principles/001-architectural-principles.md))

```
Project (the scope — contains everything)
├── Task (the primitive — first-class relational entity)
│   ├── type: agent_task | human_decision | ai_decision | action | sub_workflow
│   ├── status: proposed → approved → queued → running → completed/failed
│   ├── dependencies[], inputs, outputs, cost, duration
│   ├── source: workflow | planner | manual | ad_hoc | retry
│   ├── → TaskAttempt[] (execution attempts)
│   ├── → ProjectArtifact[] (produced by this task)
│   ├── → ProjectLearning[] (extracted from this task)
│   ├── → ProjectEvent[] (generated during this task)
│   └── → ProjectDecision[] (made during this task)
├── ProjectContext (PCD) — versioned JSON, path-based mutations
├── ProjectIntent — versioned, current + history
├── ProjectAgent — agent catalog assignments
├── ProjectMember — human assignments with roles
├── Session → SessionMessage (transport for conversations)
├── ProjectDocument → DocumentChunk — uploaded files with extraction + embeddings
├── WorkflowDefinition → WorkflowRun → WorkflowSchedule
└── HumanInputRequest — linked to human_decision Tasks
```

Supporting: User, ApiKey, AgentCatalog, PromptVersion, LlmCall, ModelPricing, Embedding, DocumentChunk.

### 5.5 Infrastructure (`AgenticWorkforce.Infrastructure`)

| Component | Technology |
|-----------|-----------|
| ORM | EF Core 9 + Npgsql (`Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4) |
| Vector search | pgvector 0.8.0 (`Pgvector.EntityFrameworkCore` 0.3.0) |
| JSON columns | Native `jsonb` with `ToJson()` complex types |
| Concurrency | `xmin` row version (optimistic) |
| Audit table | Time-partitioned (RANGE by month, BRIN index) |
| Redis | `StackExchange.Redis` — events, SignalR backplane, session cache, idempotency |
| External | Web search failover chain (Tavily → Brave → Perplexity) |
| Blob Storage | Azure Blob Storage — uploaded documents (raw + extracted), binary artifacts, audit evidence |

### 5.6 Artifacts vs Agent Outputs

Two distinct output categories with different audiences, storage, and UI treatment:

| | Artifacts | Agent Outputs |
|---|---|---|
| **What** | Polished deliverables — the reason the project exists | Raw task results — what each agent produced |
| **Who** | Stakeholders, reviewers, auditors | Operators, developers, agents |
| **Lifecycle** | Persist with the project forever | Persist with the execution record |
| **Format** | Markdown, PPTX, DOCX, XLSX, PDF, code | JSON, text, diffs, structured data |
| **Storage** | `ProjectArtifact` entity (text inline or Blob Storage URL) | `TaskExecution.OutputData` (JSON column) |
| **UI** | Artifact gallery with rendering/preview/download | Execution detail view with JSON/diff viewer |

**Artifact formats and storage:**

| Format | Storage | UI Rendering |
|--------|---------|-------------|
| Markdown | Inline `ContentText` | Rendered as HTML (Markdig) with Mermaid diagrams |
| PPTX | Azure Blob Storage (`StorageUrl`) | Download + thumbnail preview |
| DOCX | Azure Blob Storage (`StorageUrl`) | Download + preview |
| XLSX | Azure Blob Storage (`StorageUrl`) | Download + preview |
| PDF | Azure Blob Storage (`StorageUrl`) | Inline PDF viewer |
| Code | Inline `ContentText` | Syntax-highlighted viewer |
| JSON/CSV | Inline or Blob Storage | JSON tree viewer or table view |

**Document generation:** Agents produce structured content (markdown, data tables). Function tools convert to Office formats using `DocumentFormat.OpenXml` (DOCX/PPTX), `ClosedXML` (XLSX), `QuestPDF` (PDF). The agent writes the content; the tool handles format conversion.

**Agent outputs** are stored on `TaskExecution.OutputData` (JSON) with a one-line `OutputSummary` for list views. The execution detail UI shows these with JSON tree viewers, diff viewers for code changes, and pass/fail badges for verification outcomes. Each task execution links to any artifacts it produced.

### 5.7 Document Upload and Content Extraction

Users upload reference documents (policies, standards, data files, previous reports) for agents to analyse. Raw files are stored in **Azure Blob Storage** (not in the database) with metadata in PostgreSQL.

**Storage principle:** DB stays lean — only metadata, extracted text (small docs), and embeddings live in PostgreSQL. Binary bytes live in Blob Storage.

**Upload pipeline:**

```
User uploads file → API validates and stores
    │
    ├── 1. Raw file → Blob Storage (documents/{project_id}/{document_id}/original.pdf)
    ├── 2. Metadata → ProjectDocument row in PostgreSQL (name, size, hash, status: pending)
    ├── 3. Background extraction job enqueued
    │
    └── Worker picks up extraction:
        ├── 4. Extract text → PdfPig (PDF), OpenXml (DOCX/PPTX), ClosedXML (XLSX)
        ├── 5. Store extracted text → inline if <100KB, Blob if larger
        ├── 6. Chunk text (~500 tokens, paragraph boundaries, 50 token overlap)
        ├── 7. Generate embeddings → text-embedding-3-small → pgvector DocumentChunk rows
        └── 8. Update status → completed (document available to agents)
```

**Content extraction libraries:**

| Format | Library | What's Extracted |
|--------|---------|-----------------|
| PDF | `PdfPig` (open source, .NET native) | Full text, page-by-page |
| DOCX | `DocumentFormat.OpenXml` | Paragraphs, tables, headers |
| XLSX | `ClosedXML` | Sheet data as structured text/CSV |
| PPTX | `DocumentFormat.OpenXml` | Slide text, notes |
| CSV/TSV/TXT/MD | Built-in readers | Content as-is |
| Scanned PDFs / Images | Azure AI Document Intelligence (optional) | OCR text (~$1.50/1K pages) |

**How agents access uploaded documents:**

| Access Pattern | Use When | Mechanism |
|---------------|----------|-----------|
| **Context injection** | Small docs that are always relevant (policy, brief) | `ProjectContextProvider` includes tagged docs in agent context |
| **Semantic search** | Large docs or many docs — agent searches by meaning | `SearchDocuments` function tool queries `DocumentChunk` embeddings via pgvector |
| **Direct read** | Agent knows which specific document it needs | `ReadDocument` function tool fetches extracted text by ID or filename |

**Blob Storage structure:**

```
agentic-workforce-storage/
├── documents/{project_id}/{document_id}/
│   ├── original.pdf                    # raw uploaded file (preserved)
│   └── extracted.txt                   # extracted text (large docs)
├── artifacts/{project_id}/             # generated deliverables
└── audit/{region}/{yyyy/MM/dd}/        # WORM evidence files
```

### 5.8 Knowledge and Memory Management

The platform accumulates intelligence over time. See [ADR-014](ADR-014-knowledge-memory.md) for full design.

**Five knowledge types:**

| Type | What | Who Writes | Persistence |
|------|------|-----------|-------------|
| **PCD** (principles, guardrails) | Human direction that shapes agent behaviour | Humans only | Never trimmed from context |
| **Learnings** | Patterns discovered from execution (failure/success/anti-pattern/insight) | Agents (extracted after each task) | Active, retracted, or superseded |
| **Decisions** | Explicit choices with rationale | Agents (proposed) + humans (approved) | Active or superseded |
| **Intent** | Evolving understanding of the objective (versioned) | Humans + agents | Revision chain |
| **Platform Knowledge** | Learnings proven across multiple projects | Promoted by Owner, approved by Platform Admin | Read-only from project |

**Read-write cycle:**
- **Before each task:** Context assembly injects PCD, relevant learnings (by confidence + domain tags), active decisions, and for research projects, existing findings with "do not repeat" instruction
- **After each task:** `KnowledgeExtractor` (Haiku agent) analyses output, extracts learnings, deduplicates via vector similarity (>0.92 cosine = same learning → reinforce), stores new discoveries

**Deduplication:** Every new learning is embedded and compared against existing learnings. Duplicates increment `occurrence_count` and increase `confidence` rather than creating redundant entries. This prevents "agent reports the same thing 50 times."

**Human control:** Owners and Operators can retract wrong learnings (status → `retracted`, excluded from context, audit trail preserved). Owners can edit or supersede. Platform Admins approve promotions and can purge retracted learnings.

**Human direction vs agent discovery:** When a human says "always use async/await" → PCD `principles` section (instruction, absolute authority). When an agent discovers "async/await reduced errors by 30%" → `ProjectLearning` (advisory, retractable).

---

## 6. Agent Runtime Architecture

### 6.1 MAF Pipeline (per agent call)

```
[User/Workflow]
    │
    ▼
Agent Run Middleware (budget check, security gate)
    │
    ▼
ChatClientAgent core
    ├── AIContextProvider.InvokingAsync() → loads PCD, task, learnings, code map
    ├── ChatHistoryProvider.LoadAsync() → session messages
    │
    ▼
IChatClient pipeline (bottom-up execution):
    ├── OpenTelemetry middleware (traces, GenAI semantic conventions)
    ├── FunctionInvokingChatClient (tool-call loop)
    │   └── Function middleware (tool approval, argument validation)
    ├── BudgetEnforcingChatClient (per-call cost recording + enforcement)
    ├── AuditingChatClient (hash, record, non-blocking Channel<T>)
    └── Raw provider client (Anthropic Foundry / Azure OpenAI)
    │
    ▼
AIContextProvider.InvokedAsync() → rolling summary compression, persist messages
```

### 6.2 Multi-Provider Model Routing

```csharp
// Keyed DI — each provider registered separately
services.AddKeyedSingleton<IChatClient>("claude", (sp, _) =>
    new AnthropicFoundryClient(credentials)
        .AsIChatClient("claude-sonnet-4-6")
        .AsBuilder()
        .Use((inner, sp) => new BudgetEnforcingChatClient(inner, sp.GetRequiredService<IBudgetService>()))
        .Use((inner, sp) => new AuditingChatClient(inner, sp.GetRequiredService<ChannelWriter<AuditRecord>>()))
        .UseFunctionInvocation()
        .UseOpenTelemetry(sourceName: "AgenticWorkforce.Agents")
        .Build(sp));

services.AddKeyedSingleton<IChatClient>("gpt", (sp, _) =>
    new AzureOpenAIClient(endpoint, credential)
        .GetChatClient("gpt-4o-mini").AsIChatClient()
        .AsBuilder().UseOpenTelemetry().Build(sp));
```

AgentFactory resolves the correct `IChatClient` from DI based on the agent's `model_config` in the catalog.

### 6.3 Tool Integration Strategy — Container-First (Principle 22)

All tools that interact with external systems run inside containers by default. Only internal platform query tools are exempt.

**Execution Domains:**

| Domain | Where | Pattern | Examples |
|--------|-------|---------|---------|
| **Platform** (in-process) | Worker process | Native `AIFunction` calling service interfaces | `project.get_info`, `project.get_pcd`, `project.get_team`, `project.approve_tasks`, `project.get_learnings` |
| **Sandbox** (containerized) | ACA Dynamic Sessions (Hyper-V) | `AIFunction` delegating to `IDynamicSessionClient` | File ops, shell, web search (Tavily/Brave/Perplexity), Azure AI Search, SharePoint/Graph, SonarQube, Snyk, compliance API |
| **Sandbox** (containerized) | MCP server in container | JSON-RPC (stdio or HTTP) | Bandit, Semgrep, git operations, tools shared with VS Code/Copilot |
| **Remote** | Cross-service HTTP | A2A protocol (`A2AAgent` proxy) | External compliance agent, partner-team agents |

**Why web search and Azure SDK tools moved to Sandbox:** In-process network calls bypass sandbox egress controls. A prompt-injected agent calling an in-process `web.search` can make arbitrary HTTP requests from the Worker's Managed Identity. Containerized, that tool is constrained by the Azure Firewall egress allowlist. The ~10ms overhead per call is negligible compared to the LLM round-trip.

**New tools default to Sandbox.** Registering a tool as Platform requires explicit justification and an architecture test verifying no outbound network calls or filesystem access.

---

## 7. Workflow Architecture

### 7.1 Editable Workflow Graphs with Decision Points

Workflows are **stored, versioned, editable directed graphs** — created by humans (visual editor) or AI (Workflow Agent), interpreted at runtime by a generic Durable Task orchestrator. See [ADR-013](ADR-013-workflow-design.md) for full design.

**Node types:** Agent Task, Human Decision, AI Decision, Parallel Split/Join, Sub-workflow, Action, Start, End.

**Decision nodes** are the key concept — explicit points where a human or AI makes a choice that determines the path:

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

### 7.2 Runtime Execution

The `WorkflowInterpreter` reads a `WorkflowDefinition` (JSON graph from DB) and walks it:

```
WorkflowDefinition (stored graph)
    │
    ▼
WorkflowInterpreter (generic Durable Task orchestrator)
    │
    ├── Agent Task nodes → MAF agent.RunAsync() inside Durable Task activities
    ├── Human Decision nodes → WaitForExternalEvent<string>(timeout) + SignalR notification
    ├── AI Decision nodes → MAF agent.RunAsync<StructuredDecision>() with constrained schema
    ├── Parallel Split → fan out to child orchestrations
    ├── Sub-workflow → invoke another WorkflowDefinition recursively
    └── Edge routing → follow the edge whose condition matches the node's output
```

**Key property:** New workflows execute without code deployment — create in the visual editor, run immediately.

### 7.3 Authoring

| Method | How |
|--------|-----|
| **Visual graph editor** | React Flow canvas — drag nodes, connect edges, configure properties |
| **Workflow Agent** | Chat-based design — agent proposes graph, user refines, saves on approval |
| **Templates** | Platform-wide workflow definitions (`project_id = null`) instantiated into projects |

### 7.2 Budget Enforcement Flow

```
Per-call:     BudgetEnforcingChatClient → record cost → check cumulative
Per-agent:    Hard stop at $1.00 ceiling (configurable)
Per-session:  Warning at 80%, hard stop at 100% ($50 default)
Per-project:  Warning at 80%, hard stop at 100%
Platform:     Hourly alert at $5/hr (no auto-stop)
Emergency:    Admin pause all autonomous executions

No model downgrade — fail fast and loud. Accuracy and predictability are paramount.
```

---

## 8. Security Architecture

### 8.1 Authentication

| Consumer | Mechanism |
|----------|-----------|
| Bank employees (SPA, CLI) | Entra ID SSO (MSAL.js / MSAL.NET) |
| Programmatic consumers | API Key (`X-Api-Key` header, hashed+salted in DB) |
| SSE connections | Short-lived Redis token (30s TTL, single-use, `GETDEL`) |
| Service-to-service | User-Assigned Managed Identity (no stored secrets) |

Dual scheme via `AddPolicyScheme("JwtOrApiKey", ...)` — single `[Authorize]` accepts either.

### 8.2 Authorization — Two-Dimensional Role Model

**Platform Roles** (Entra ID App Roles — global):

| Role | What They Do |
|------|-------------|
| `platform_admin` | Agent catalog, templates, model governance, user management, platform config, emergency stop, cross-project audit |
| `member` | Baseline authenticated user. Can be assigned to projects. |

**Project Roles** (per-project, `project_members` table):

| Role | Can Run | Can Approve | Can Manage | Can View |
|------|---------|-------------|------------|----------|
| `owner` | Yes | Yes | Yes (team, budget, delete) | Yes |
| `operator` | Yes | Yes | No | Yes |
| `reviewer` | **No** | **Yes** | No | Yes |
| `viewer` | No | No | No | Yes |

**Segregation of duties:** Reviewer cannot run executions. The person who triggers cannot approve their own execution. Platform Admin can access all projects (override, audited).

### 8.3 Zero-Secret Infrastructure

All Azure service auth via Managed Identity:

| Resource | RBAC Role |
|----------|-----------|
| Azure AI Foundry | Cognitive Services OpenAI User |
| Key Vault | Key Vault Secrets User |
| Blob Storage (audit) | Storage Blob Data Owner |
| Container Registry | AcrPull |
| Dynamic Sessions | Azure ContainerApps Session Executor |

### 8.4 Agent Sandbox Isolation

- ACA Dynamic Sessions with **Hyper-V isolation** per project
- Network egress disabled by default (Azure Firewall allowlist for required endpoints)
- Per-project session ID: `HMAC-SHA256(projectId, tenantId, agentRunId)` — server-generated, never from LLM
- 24-hour max session; ACI fallback for longer projects

---

## 9. Audit and Compliance Architecture

### 9.1 Audit Pipeline

```
Agent execution → IChatClient middleware (backpressure Channel<T>, Wait + 5s timeout)
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

### 9.2 Tamper Detection

Per-stream SHA-256 hash chain with sequence numbers. Chain state (`seq` + `prevHash`) persisted in Redis via atomic Lua scripts — survives pod restarts without breaking chain continuity. Daily Merkle root anchored to WORM blob. Verification: linear pass checking sequence continuity + hash chain integrity via KQL.

### 9.3 Regulatory Mapping

| Requirement | Mechanism |
|-------------|-----------|
| SS1/23 model decision evidence | WORM Blob + hash chain + KQL queryable store |
| FG16/5 cloud outsourcing | Azure FCA/PRA compliance offering + SOC/ISO attestations |
| POPIA data residency | SA North ZRS storage, no GRS cross-jurisdiction |
| 7-year retention | Blob WORM locked + Eventhouse SoftDeletePeriod |
| SEC 17a-4(f) WORM attestation | Cohasset Associates independent attestation |

### 9.4 Cost

~$600/month for both regions (~$0.83 per 1,000 audited turns).

---

## 10. Observability

### 10.1 Project Console — Log Everything, Blacklist Noise

Every project has a **console view** — a real-time, scrollable, filterable timeline of every action and event happening in the project. This is the primary observability surface for operators and reviewers.

**Principle:** Log everything by default. Exclude noisy events via a configurable blacklist (`exclude_event_types` in config) — never whitelist. Errors surface immediately, not buried in logs.

**Dual persistence:**

| Destination | Format | Purpose | Latency |
|-------------|--------|---------|---------|
| JSONL file (`var/logs/events.jsonl`) | Append-only structured JSON lines | Fast local capture, survives DB outage | Synchronous |
| `ProjectEvent` table (PostgreSQL) | Structured entity (`event_type`, `source`, `data` JSON, `timestamp`) | Queryable, filterable, paginated API | Async (non-blocking) |
| SignalR (`project:{id}` channel) | Real-time push to connected clients | Console view in React frontend | Real-time |

**Event categories:**

| Category | Examples | Default |
|----------|---------|---------|
| Agent lifecycle | `agent.started`, `agent.completed`, `agent.failed` | Shown |
| Tool invocations | `tool.called`, `tool.result`, `tool.error` | Shown |
| LLM calls | `llm.request`, `llm.response`, `llm.error` | Shown |
| Workflow state | `workflow.started`, `node.entered`, `node.completed`, `decision.made` | Shown |
| Human decisions | `gate.requested`, `gate.approved`, `gate.rejected`, `gate.timeout` | Shown |
| Budget events | `budget.warning`, `budget.exceeded` | Shown |
| Context changes | `pcd.updated`, `learning.created`, `artifact.created` | Shown |
| Errors | Any `*.error` or `*.failed` event | **Always shown — never blacklisted** |
| Streaming chunks | `agent.response.chunk` | **Blacklisted** (too noisy) |
| Cost micro-updates | `session.cost.update` | **Blacklisted** |
| Keepalive pings | `stream.keepalive` | **Blacklisted** |

**The blacklist is configurable per project** — an operator can choose to see streaming chunks for debugging, then turn them off again.

### 10.2 OpenTelemetry (built-in via MAF + Aspire)

| Span | Source | Attributes |
|------|--------|-----------|
| `invoke_agent` | Agent run | agent name, duration |
| `chat` | Model call | model, input/output tokens, finish reason |
| `execute_tool` | Tool invocation | tool name, duration |

Activity sources: `*Microsoft.Extensions.AI`, `*Microsoft.Extensions.Agents*`.

### 10.2 Health Probes

| Probe | Path | Checks |
|-------|------|--------|
| Startup | `/health/ready` | DB + Redis + LLM (150s budget for cold start) |
| Liveness | `/alive` | Process-only |
| Readiness | `/health/ready` | DB + Redis + LLM endpoint |

### 10.3 Custom Banking Metrics

```
Meter: AgenticWorkforce.Cost
├── llm.cost.usd (counter, tags: project_id, agent, model)
├── llm.tokens.total (counter, tags: direction, model)
└── llm.calls.count (counter, tags: agent, model, status)
```

---

## 11. IaC and CI/CD

### 11.1 Two-Layer Bicep Strategy

| Layer | Source | Modules |
|-------|--------|---------|
| Platform (Avalanche) | Avalanche scaffold | VNet, NSGs, DNS, KV, Log Analytics, App Insights, ACA Environment |
| Application (Aspire) | `azd infra synth` | Container Apps, ACR, identities, role assignments, probes |

Avalanche outputs → Aspire inputs via well-known parameters (`managedEnvironmentId`, `KeyVaultName`, `LogAnalyticsWorkspaceId`). Aspire's `RunAsExisting` / `PublishAsExisting` binds to Avalanche-provisioned resources.

### 11.2 Pipeline Stages

```
validate-bicep → provision-platform (Avalanche) → provision-app (azd provision)
    → build-push-images → deploy-app (azd deploy) → smoke-tests
```

Three pipelines from Avalanche: `azure-deploy.yml`, `azure-pr-validation.yml`, `azure-cleanup.yml`.

---

## 12. Architectural Principles

This architecture is governed by **22 non-negotiable principles** defined in [001-architectural-principles.md](../003-principles/001-architectural-principles.md). Every ADR includes a Principle Compliance section verifying alignment. Key principles that shape this architecture:

| # | Principle | Architectural Impact |
|---|-----------|---------------------|
| 1 | Task is the primitive | Task is a first-class relational entity; everything hangs off it |
| 2 | Project is the scope | Every entity has `project_id` FK; cascading deletes scope everything |
| 4 | Wrap the core | MAF, Foundry, EF Core, SignalR behind our own interfaces |
| 8 | Fail fast, never degrade | Budget exceeded = exception, not model downgrade |
| 14 | Secure by default (Unix model) | Empty allowlist = deny all; new features disabled by default |
| 15 | Backend owns all logic | Clients display; all decisions server-side |
| 16 | Single source of truth | PostgreSQL is authoritative; Redis is ephemeral cache |
| 17 | Human authority | Humans override any agent decision; escalate when uncertain |
| 18 | Idempotency | Safe to retry everywhere; Durable Task activities replay-safe |
| 19 | Bounded resource usage | Every operation has a timeout, limit, or max size |
| 22 | Container-first tool execution | All tools touching network/filesystem run in Dynamic Sessions; only `project.*` tools are in-process |

---

## 14. Key Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| MAF sub-packages still preview (Anthropic, DurableTask, A2A) | API surface may shift | Pin versions in `Directory.Packages.props`; isolate behind thin abstractions |
| Aspire has no LTS | Only latest release supported | Plan minor-version upgrades every 6 months |
| Foundry Anthropic is preview; inference on Anthropic infra (not Azure region) | Data residency for PII workloads | Architecture supports provider swap as config change; evaluate Bedrock EU if needed |
| No local Claude tokenizer | Token budget estimation requires API call | Cache aggressively; calibrate offline linear estimator as fallback |
| No built-in Azure AI audit trail | Must build entire evidence subsystem | Custom audit pipeline (ADR-008) with WORM + hash chain |
| Two Bicep layers must compose | Duplicate resource errors if not coordinated | `RunAsExisting` pattern; parameter contracts as interface |

---

## 15. Architecture Decision Record Index

| ADR | Decision | Document |
|-----|----------|----------|
| 001 | Durable Task SDK + DTS (outer) + MAF Workflows (inner) | [ADR-001](ADR-001-workflow-engine.md) |
| 002 | Foundry Anthropic for Claude + Azure OpenAI for embeddings | [ADR-002](ADR-002-llm-provider-strategy.md) |
| 003 | DB-driven AgentFactory over sealed ChatClientAgent | [ADR-003](ADR-003-agent-model-design.md) |
| 004 | PostgreSQL Flexible Server + pgvector (single DB) | [ADR-004](ADR-004-data-layer.md) |
| 005 | Self-hosted SignalR + Redis + plain SSE | [ADR-005](ADR-005-realtime-events.md) |
| 006 | ACA Dynamic Sessions (Hyper-V custom containers) | [ADR-006](ADR-006-container-isolation.md) |
| 007 | Entra ID + API Key dual scheme + Managed Identity | [ADR-007](ADR-007-identity-auth.md) |
| 008 | Middleware → Event Hubs → Blob WORM + Eventhouse | [ADR-008](ADR-008-audit-compliance.md) |
| 009 | DelegatingChatClient middleware with hierarchical budgets | [ADR-009](ADR-009-cost-tracking.md) |
| 010 | AIContextProvider + ContextAssembler with token budgets | [ADR-010](ADR-010-context-assembly.md) |
| 011 | Native AIFunction + MCP containers + A2A for cross-team | [ADR-011](ADR-011-mcp-a2a.md) |
| 012 | Separate Api + Worker, Vertical Slice, Avalanche + Aspire Bicep | [ADR-012](ADR-012-aspire-integration.md) |
| 013 | Editable workflow graphs with Human and AI decision points | [ADR-013](ADR-013-workflow-design.md) |
| 014 | Knowledge and memory management with read-write cycle and deduplication | [ADR-014](ADR-014-knowledge-memory.md) |
| 015 | Knowledge Graph layer (Apache AGE) — deferred to Phase 2 | [ADR-015](ADR-015-knowledge-graph.md) |
| 016 | Agent Design — catalog, prompts, tools, constraints, lifecycle | [ADR-016](ADR-016-agent-design.md) |
| 017 | Project Orchestration — Director + Dispatch Engine + Supervisor | [ADR-017](ADR-017-project-orchestration.md) |

---

## 16. Key Package Dependencies

```xml
<!-- Agent Framework -->
<PackageReference Include="Microsoft.Agents.AI" Version="1.5.0" />
<PackageReference Include="Microsoft.Agents.AI.Anthropic" Version="--prerelease" />
<PackageReference Include="Microsoft.Agents.AI.Foundry" Version="--prerelease" />
<PackageReference Include="Microsoft.Agents.AI.Workflows" />
<PackageReference Include="Microsoft.Extensions.AI" Version="10.5.2" />
<PackageReference Include="Anthropic.Foundry" Version="--prerelease" />

<!-- Durability -->
<PackageReference Include="Microsoft.DurableTask.Worker.AzureManaged" Version="1.19.0" />
<PackageReference Include="Microsoft.DurableTask.Client.AzureManaged" Version="1.19.0" />

<!-- Data -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />

<!-- Auth -->
<PackageReference Include="Microsoft.Identity.Web" Version="4.9.0" />
<PackageReference Include="Azure.Identity" Version="1.21.0" />

<!-- Real-time -->
<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />

<!-- Tokenization -->
<PackageReference Include="Microsoft.ML.Tokenizers" Version="2.0.0" />

<!-- MCP -->
<PackageReference Include="ModelContextProtocol" Version="--prerelease" />

<!-- A2A -->
<PackageReference Include="Microsoft.Agents.AI.A2A" Version="--prerelease" />
```
