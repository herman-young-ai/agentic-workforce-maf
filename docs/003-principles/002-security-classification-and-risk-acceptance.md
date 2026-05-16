# Agentic Workforce Platform — Security Classification & Risk Acceptance

**Document type:** AI Risk Classification (Framework objective 1.1)
**System name:** Agentic Workforce Platform (AWP)
**Version:** 1.0
**Date:** 2026-05-11
**Author:** Platform Architecture Team
**Accountable individual:** [TBD — named CTO-line senior individual per SMCR]
**Review cadence:** Quarterly (minimum), or on any architecture change to scope, data sources, tool access, or downstream consumers
**Next review:** 2026-08-11

**Framework reference:** [AI Security Control Framework](../../../ai-security-control-framework/) — objectives 1.1, 1.2, 1.3, 1.4, 1.5, 2.2, 2.4
**Security architecture:** [R17 Security Architecture](../098-research/R17-response-security-design.md)

---

## 1. System Description

The Agentic Workforce Platform deploys teams of AI agents (Claude, GPT) to execute tasks within projects for a dual-regulated bank (Investec — FCA/PRA in UK, SARB/PA in South Africa). Agents can read/write files, execute shell commands in sandboxed containers, search the web, query databases, call internal bank APIs, and produce reports used by regulators. Humans create projects, design workflows, approve agent outputs, and upload documents for analysis. The platform accumulates knowledge (learnings, decisions, context) that shapes future agent behaviour. All actions are audited with 7-year immutable retention.

**Deployment:** Azure Container Apps (SA North / UK South)
**Models consumed:** Claude (Anthropic via Azure AI Foundry), GPT-4o + text-embedding-3-small (Azure OpenAI)
**Model relationship:** API consumption only — no training, fine-tuning, or weight access

---

## 2. Tier Classification (Objective 1.1)

### Classification: **Tier 4 — Open-ended multi-step agentic**

| Tier 4 Criterion | AWP Evidence |
|-------------------|-------------|
| Dynamic tool selection | Agents select tools from their manifest based on task context. Tool selection is LLM-driven, not hardcoded. |
| Cross-session memory | Project Context Document (PCD) and learnings persist across executions and shape future agent behaviour (ADR-014). |
| Sub-agent spawning | Workflow orchestrator (Durable Task) can decompose tasks and dispatch to multiple agents within a project. |
| Multi-step execution | Agents execute multi-step workflows with branching, loops, and conditional logic (ADR-013). |

### Modifier Assessment

| Modifier | Applies | Justification |
|----------|---------|---------------|
| **Trust-laundering** | **Yes** | Agent output feeds tool execution sinks (shell commands, API calls, file writes) via `FunctionInvokingChatClient`. The execution sink (Dynamic Sessions) does not independently validate the semantic correctness of commands — it executes what the agent requests. The combined system inherits Tier 4. **Compensating control:** All execution happens in Hyper-V isolated sandboxes with network egress disabled (R17 C19). Tool manifest is server-set and immutable (R17 C20). |
| **Blast-radius** | **Yes** | The platform is multi-project. A compromised agent could theoretically affect the project's data, PCD, and learnings. Cross-project isolation is enforced via `project_id` partitioning at every layer (R17 C13). **Not cross-tenant** (single-tenant deployment per bank). |
| **Aggregate blast-radius** | **Yes** | Per-action bound: agent can execute shell commands, write files, produce reports. Frequency: multiple tasks per project, multiple projects per day. Time window: continuous operation. Aggregate exposure exceeds material thresholds. **Compensating control:** Budget enforcement (ADR-009, fail fast never degrade), guardrail enforcement (R17 C22), emergency stop (R17 C31). |
| **Regulated-omission** | **Conditional** | Currently not applicable — AWP workflows are research, analysis, and reporting. **Trigger:** If the AWP is used for sanctions screening, SAR filing, best-execution monitoring, or any process where failure to act triggers regulatory liability, this modifier activates and requires additional governance (minimum quarterly review of agent reliability metrics for the regulated workflow). |
| **Decision-influence** | **Yes** | Agent outputs include reports used by regulators, security findings used to prioritise remediation, and analysis used to inform business decisions. These outputs materially influence consequential decisions even though the agent does not execute the decision directly. **Compensating control:** Human approval gates on all workflow outputs (R17 C36, segregation of duties). Content Safety filtering (R17 C21). |

### Governance Implications of Tier 4

Per the framework, Tier 4 classification requires:
- Formal architecture review with documented evidence (not checklist self-certification)
- Dual independent challenger sign-off (CTO line + CISO line + CRO line)
- Named senior accountable individual documented
- OPA/Cedar policy engine for runtime permission enforcement (R17 gap G2 — planned)
- Full agent scope and permission review (Section 5 below)
- All Theme 5 security testing (AI-specific CI/CD testing — R17 gap, planned)
- Registration in Group AI registry (Section 7 below)

---

## 3. Lethal Trifecta Assessment (Objective 1.2)

### Three-Property Test

| Property | Present | Evidence |
|----------|---------|----------|
| **1. Accesses private or sensitive data** | **Yes** | Agents access uploaded client documents (Restricted classification), project context documents, learnings, and chat messages. Documents may contain PII, financial data, and regulatory content. Data classification: Restricted and Confidential (R17 Area 5 §1). |
| **2. Processes untrusted content** | **Yes** | Agents process user-uploaded documents, user chat messages, and web search results. Uploaded documents are a confirmed indirect prompt injection vector (R17 threat 4.2, rated H/H). |
| **3. Can take external actions** | **Yes** | Agents execute shell commands in sandboxed containers, read/write files, call internal APIs, and produce reports. Actions are mediated through tool functions (ADR-006). |

### Result: All three properties present

**Architectural elimination assessment:** Can any property be removed?

| Property | Can it be eliminated? | Assessment |
|----------|----------------------|------------|
| Private data access | **No** | The platform's purpose is to analyse documents and produce insights. Removing data access removes the use case. |
| Untrusted content processing | **No** | Users must be able to upload documents and provide instructions. This is a core workflow. |
| External actions | **Partially** | Could restrict to read-only (Tier 1) but this eliminates the platform's value proposition — agents need to write files, execute code, and produce artifacts. |

**Conclusion:** Architectural elimination is not feasible. The platform requires all three properties to fulfil its purpose. **Executive risk acceptance is required.**

### Compensating Controls

| Trifecta Property | Risk | Compensating Control | R17 Reference | Evidence of Effectiveness |
|-------------------|------|---------------------|---------------|--------------------------|
| Private data → exfiltration | Agent exfiltrates data via tool calls or search queries | Hyper-V sandbox with network egress disabled by default. Azure Firewall FQDN allowlist. PII detection before storage. | C19, C28, C24 | Pen test scope includes data exfiltration (R17 Area 8 §4). Firewall rules auditable. |
| Private data → cross-project leakage | Agent context leaks across projects | `project_id` partitioning at every layer (DB, Redis, pgvector, Blob). Cross-project promotion requires platform_admin approval. | C13 | Pen test scope includes cross-project access. |
| Untrusted content → prompt injection | Uploaded document overrides agent behaviour | 5-layer defence: input sanitisation, system prompt hardening, structured output parsing, canary detection, HITL gates. | C15 | Red team exercise scope includes document injection, multi-turn injection. |
| Untrusted content → learning poisoning | Malicious content persists as knowledge | Learning human gate: all agent-extracted learnings start as `pending`, require human promotion to `active`. 30-day auto-expiry. | C16 | Process audit. ADR-014 updated. |
| External actions → destructive commands | Agent executes harmful shell commands | Sandbox-only execution (Hyper-V). Command blocklist. 5-min timeout per command. Tool manifest server-set and immutable. | C19, C20 | Pen test scope includes agent tool abuse. |
| External actions → unauthorised API calls | Agent calls APIs outside its scope | Tool manifest per agent — enumerated, immutable, server-set. Guardrail enforcement blocks unlisted tool calls. | C20, C22 | Unit tests + code review. |
| All three combined | Chained attack across properties | Budget enforcement (fail fast). Emergency stop (Redis kill switch). Content Safety filtering. Full audit trail (WORM, hash chain). | C31, C21, C33 | Quarterly emergency stop drill. Daily hash chain verification. |

### Review Cadence

Quarterly review of this assessment, or immediately upon:
- New tool types added to agent capabilities
- New data sources connected
- New downstream consumers of agent outputs
- Any security incident involving agent behaviour

### Kill-Switch and Fallback

| Mechanism | Scope | Trigger | Recovery |
|-----------|-------|---------|----------|
| **Project-level emergency stop** | Single project | Project `owner` via API or UI | `POST /api/v1/admin/emergency/resume` by `platform_admin` |
| **Platform-level emergency stop** | All projects | `platform_admin` (PIM-activated) via API | Same endpoint with confirmation body |
| **Budget kill-switch** | Per-project or per-task | Automatic when budget ceiling exceeded | Manual budget increase by project `owner` |
| **Content Safety block** | Per-agent-response | Automatic when severity threshold exceeded | Owner reviews and adjusts guardrails |
| **Guardrail violation block** | Per-tool-call | Automatic when rule violated | Owner reviews PCD guardrails |

**Fallback procedure:** If the AWP is unavailable, the business processes it automates revert to manual execution by the same team members who would otherwise review agent outputs. The platform accelerates work — it does not replace capabilities that don't exist without it. Manual fallback is viable because the platform is additive, not substitutive, at current maturity.

---

## 4. Trust Boundary and Data Flow (Objective 1.3)

### End-to-End Data Flow

```
                    ┌─── Trust Boundary: Internet ───┐
                    │                                  │
                    │  SPA (browser) / CLI             │
                    │  Entra ID JWT / API Key auth     │
                    └──────────┬───────────────────────┘
                               │ HTTPS (TLS 1.2+)
                               ▼
                    ┌─── Trust Boundary: Azure Front Door + APIM ───┐
                    │  WAF (OWASP 3.2), DDoS Protection             │
                    │  OAuth validation at edge                      │
                    └──────────┬────────────────────────────────────┘
                               │
                               ▼
┌─── Trust Boundary: ACA VNet (Private) ─────────────────────────────────────┐
│                                                                              │
│  BFF API (AgenticWorkforce.Api)                                             │
│  ├── Rate limiting, input validation, security headers                      │
│  ├── ProjectRoleAuthorizationHandler (role hierarchy + temporal expiry)     │
│  ├── Idempotency middleware (fail-open)                                     │
│  └── Redis Streams XADD (work dispatch to Worker)                          │
│                                                                              │
│  Worker (AgenticWorkforce.Worker)                                           │
│  ├── Durable Task orchestrator (workflow execution)                         │
│  ├── IChatClient pipeline:                                                  │
│  │   Budget → Audit → FunctionInvocation → ContentSafety → OTel → Model   │
│  ├── ContextAssembler (PCD + learnings + decisions → agent context)         │
│  ├── KnowledgeExtractor (output → pending learnings)                       │
│  └── GuardrailEnforcementMiddleware (pre-tool-call validation)             │
│                                                                              │
│  ┌─── Trust Boundary: Dynamic Sessions (Hyper-V) ──────────────────┐       │
│  │  Per-project sandbox (isolated filesystem, network egress off)   │       │
│  │  Shell commands, file R/W, git operations                        │       │
│  │  Egress (when enabled): Azure Firewall FQDN allowlist only      │       │
│  └──────────────────────────────────────────────────────────────────┘       │
│                                                                              │
│  Private Endpoints:                                                         │
│  ├── PostgreSQL (project data, learnings, PCD) ── CMK encrypted            │
│  ├── Redis (cache, streams, pub/sub, kill switch)                          │
│  ├── Blob Storage (audit WORM, documents) ── CMK encrypted                 │
│  ├── Event Hubs (audit transport)                                          │
│  ├── Key Vault (secrets, RBAC mode, MI only)                               │
│  └── Azure AI Foundry (Claude, GPT) ── private endpoint                    │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Data Inputs, Sensitivity, and Trust Level

| Input Source | Data Type | Sensitivity | Trust Level |
|-------------|-----------|-------------|-------------|
| User chat messages | Natural language instructions | Confidential | Semi-trusted (authenticated user, but could contain injection) |
| Uploaded documents | PDF, DOCX, XLSX, CSV, TXT, MD, JSON | Restricted (may contain PII, financial data) | **Untrusted** (indirect injection vector) |
| Web search results | External web content | Internal | **Untrusted** |
| PCD (principles, guardrails) | Human-authored project direction | Confidential | Trusted (human-only write paths) |
| PCD (context, current_state) | Agent-updated project context | Confidential | Semi-trusted (agent-written, auditable) |
| Learnings (active) | Human-promoted knowledge | Confidential | Trusted (passed human gate) |
| Learnings (pending) | Agent-extracted knowledge | Confidential | **Untrusted** (not yet human-reviewed) |
| Platform learnings | Cross-project promoted knowledge | Confidential | Trusted (platform_admin approved) |

### Data Outputs and Destinations

| Output | Destination | Validation Before Delivery |
|--------|------------|---------------------------|
| Agent chat responses | Human (SPA/CLI via SSE) | Content Safety scan (C21) |
| Reports / artifacts | Human review (SPA download) | Content Safety scan (C21) |
| Shell command execution | Dynamic Sessions sandbox | Guardrail enforcement (C22), sandbox isolation (C19) |
| File writes | Dynamic Sessions sandbox filesystem | Path allowlist, sandbox isolation |
| PCD updates (writable paths) | PostgreSQL | Path allowlist (C17), diff logged |
| Pending learnings | PostgreSQL | Human gate required before inclusion in context (C16) |
| Audit records | Event Hubs → Blob WORM + Eventhouse | Hash chain (C33), immutable |

### Trust Boundary Validations

| Validation | Status | Evidence |
|-----------|--------|----------|
| **Vector ACL parity** (Knostic problem) | **Passes** | pgvector embeddings partitioned by `project_id`. Similarity search includes `WHERE project_id = @pid`. Project membership (role-based) controls who can query. Vector ACLs match project-level access controls. No coarser-than-source ACL issue. |
| **Trust-laundering test** | **Documented** | Agent output feeds shell execution in Dynamic Sessions. The sandbox does not validate semantic correctness — it executes what the agent requests. Compensating controls: Hyper-V isolation, network egress disabled, command blocklist, tool manifest, guardrail enforcement. Documented and accepted in trifecta assessment (Section 3). |
| **Information barrier compliance** | **Not applicable currently** | The AWP is not deployed across wall-crossed business functions. Project membership is explicit and auditable. If the AWP is used by both investment banking and research functions, information barrier enforcement must be added to project membership rules. |
| **Tenant isolation** | **Passes** | Single-tenant deployment per bank. Cross-project isolation enforced at every layer (R17 C13). |

---

## 5. Agent Scope and Permission Review (Objective 1.4)

### Platform-Level Agent Capabilities

| Capability | Scope | Credentials | Blast Radius (single action) |
|-----------|-------|-------------|------------------------------|
| **Shell execution** | Commands run in per-project Dynamic Sessions sandbox. Hyper-V isolated. | Managed Identity (Azure ContainerApps Session Executor) | Sandbox filesystem destroyed. No host or cross-project impact. |
| **File read/write** | Within sandbox filesystem only (per-project). | Via Dynamic Sessions API | File corruption within project sandbox. Recoverable (session recreatable). |
| **Web search** | Query external search APIs. | API key (scoped, rotated). | Data exfiltration via search queries (mitigated by egress controls + query logging). |
| **Database query** | Project-scoped data only. `WHERE project_id = @pid` enforced. | Connection via project-scoped context, not direct DB credentials. | Data read within project scope. No write access to DB from agent tools. |
| **API calls** | Only tools in the agent's manifest. Manifest is server-set, immutable. | Project-scoped credentials. | Depends on API — scoped by tool manifest. |
| **PCD write** | Writable paths only (context, current_state). Principles and guardrails are readonly. | Via PcdUpdateTool with path allowlist. | PCD context corruption within project. Recoverable (audit diff + revert). |
| **Learning extraction** | Agent proposes learnings. All start as `pending`. | Via KnowledgeExtractor. | No direct impact — pending learnings excluded from context until human-promoted. |

### Aggregate Blast-Radius Calculation

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Per-action bound | Sandbox-scoped (files, shell) or project-scoped (PCD, learnings) | No action can affect other projects or host infrastructure |
| Frequency | ~50-200 tool calls per task execution | Based on typical agent execution patterns |
| Tasks per project per day | ~5-20 (varies by project type) | Research projects: fewer, longer tasks. Monitoring projects: many short tasks. |
| Active projects | ~10-50 at steady state | Initial deployment |
| Time window | Continuous (24/7) | Automated workflows may run overnight |
| **Aggregate daily exposure** | ~2,500-200,000 tool calls across all projects | Each scoped to its project sandbox |
| **Material threshold** | Exceeds material threshold due to volume | **Compensating controls:** Budget enforcement halts at ceiling. Emergency stop available. All actions audited. |

### Regulated-Omission Check

| Question | Answer |
|----------|--------|
| Does the AWP perform sanctions screening? | **No** (current scope) |
| Does the AWP file SARs? | **No** |
| Does the AWP monitor best-execution? | **No** |
| Does the AWP perform any function where failure to act triggers regulatory liability? | **No** (current scope) |
| **Trigger for re-assessment:** | If any workflow is created that touches AML, sanctions, KYC, SAR, or best-execution, this modifier activates immediately. Project templates that touch these functions should be flagged in the agent catalog. |

### Decision-Influence Check

| Question | Answer |
|----------|--------|
| Do agent outputs influence consequential decisions? | **Yes** — reports are used by regulators, security findings inform remediation priorities |
| Are agent outputs the sole basis for decisions? | **No** — all outputs pass through human approval gates (segregation of duties) |
| **Compensating controls:** | HITL approval gates (C36), Content Safety (C21), full audit trail (C33) |

---

## 6. EU AI Act Article 5 Prohibited-Practice Screening (Objective 2.4)

| Prohibited Practice | AWP Assessment | Status |
|---------------------|---------------|--------|
| **Emotion recognition in the workplace** | The AWP does not analyse facial expressions, voice tone, body language, or physiological signals of employees or users. | **Not performed** |
| **Social scoring** | The AWP does not score individuals based on social behaviour, personality traits, or inferred characteristics. | **Not performed** |
| **Biometric categorisation by sensitive attributes** | The AWP does not categorise individuals by race, political opinion, trade union membership, sexual orientation, or religious beliefs. | **Not performed** |
| **Manipulative or deceptive AI techniques** | The AWP does not generate content designed to manipulate user behaviour or exploit vulnerabilities. Agents operate under explicit human-authored instructions (PCD principles and guardrails). | **Not performed** |
| **Real-time remote biometric identification** | The AWP does not perform facial recognition, gait analysis, or any biometric identification. | **Not performed** |

**Screening result:** No Article 5 prohibited practices identified.
**Re-screening trigger:** When new agent capabilities, tool types, or use cases are added. When EU AI Act enforcement guidance is updated.

---

## 7. Group AI Registry Entry (Objective 2.2)

Pre-filled fields for ServiceNow CMDB registration:

| Field | Value |
|-------|-------|
| **System name** | Agentic Workforce Platform (AWP) |
| **Owner** | Platform Architecture Team |
| **Named accountable individual** | [TBD — CTO-line SMF holder] |
| **Tier classification** | Tier 4 — Open-ended multi-step agentic |
| **Applicable modifiers** | Trust-laundering, Blast-radius, Aggregate blast-radius, Decision-influence. Regulated-omission: conditional (not currently active). |
| **Intended use** | AI agent orchestration for research, analysis, security review, and reporting tasks within regulated banking operations |
| **Approved scope** | Projects created by authorised members. Agent capabilities defined by server-set tool manifests. Outputs require human approval. |
| **Data sources** | User-uploaded documents (Restricted), chat messages (Confidential), web search (Internal), PCD/learnings (Confidential), platform learnings (Confidential) |
| **Data sensitivity** | Restricted (uploaded documents, audit records) + Confidential (PCD, learnings, cost data) |
| **Deployment environment** | Azure Container Apps (SA North, UK South). Private endpoints. VNet-integrated. |
| **Models consumed** | Claude Sonnet 4.6, Claude Haiku 4.5, Claude Opus 4.6 (Anthropic via Azure AI Foundry); GPT-4o, text-embedding-3-small (Azure OpenAI) |
| **Build / Buy / Use** | Build (platform) + Use (API-consumed models) |
| **Vendor providers** | Anthropic (models via Azure AI Foundry), Microsoft (Azure infrastructure + OpenAI models) |
| **Approval status** | [Pending — requires dual sign-off per objective 1.5] |
| **Last validation date** | [TBD — initial assessment] |
| **Next scheduled validation** | [TBD + 90 days] |
| **Current status** | Development |
| **Lethal trifecta** | All three properties present. Executive risk acceptance required. Compensating controls documented in Section 3. |
| **Article 5 screening** | Passed — no prohibited practices. |
| **Kill-switch** | Platform-level and project-level emergency stop documented in Section 3. |
| **Security architecture** | [R17 Security Architecture](../098-research/R17-response-security-design.md) — 40 controls. |

---

## 8. Pre-Production Sign-Off Checklist (Objective 1.5)

| Requirement | Status | Evidence |
|-------------|--------|----------|
| Tier classification current and correctly applied (1.1) | Complete | Section 2 above |
| Lethal trifecta assessment complete (1.2) | Complete | Section 3 above |
| Trust boundaries and data flows documented (1.3) | Complete | Section 4 above |
| Agent permissions scoped and justified (1.4) | Complete | Section 5 above |
| AI-specific security testing complete (Theme 5) | **Pending** | Pen test scope defined (R17 Area 8 §4). Automated CI/CD testing (Garak/Promptfoo) not yet implemented. |
| Model validation complete (Theme 9) | N/A | API consumption only — no internal model validation required. |
| System registered in AI inventory (Theme 2) | **Pending** | Registry entry pre-filled (Section 7). ServiceNow CMDB submission required. |
| Named senior accountable individual documented | **Pending** | [TBD — requires CTO-line SMF holder assignment] |

### Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| **CTO line** (developing team) | | | |
| **CISO line** (independent challenger — security) | | | |
| **CRO line** (independent challenger — risk) | | | |

> This document must be signed by all three parties before the AWP deploys to production. Sign-off records are retained for the lifetime of the system plus the regulatory retention period (7 years). Per objective 1.5, audit can trace the AWP back to its approval chain within 24 hours.

---

## 9. Review Log

| Date | Reviewer | Change | Trigger |
|------|----------|--------|---------|
| 2026-05-11 | Platform Architecture Team | Initial classification and assessment | Framework alignment (R17a) |
