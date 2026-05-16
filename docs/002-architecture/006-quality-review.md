# Architecture Quality Review

**Date:** 2026-05-12
**Reviewer:** Architecture Team
**Scope:** Full architecture — 15 ADRs, Solution Architecture, DB Schema, API Design, Security Design, UX Requirements, Principles
**Verdict:** **PASS — ready for implementation with 3 simplifications and 3 pre-coding gaps**

---

## 1. Modularity

**Assessment: GOOD**

Project structure (7 projects) with clean one-way dependency graph:

```
AppHost → ServiceDefaults
              ↓
         Api ─────→ Interfaces (Domain)
         Worker ───→ Interfaces (Domain)
                        ↑
         Agents ────── implements IAgentRuntime (wraps MAF)
         Infrastructure implements IProjectRepository (wraps EF Core)
```

No cycles. No skipped layers. Each project has a single responsibility.

**One observation:** The `Agents` project has two responsibilities — wrapping MAF and assembling domain context. This is acceptable because it IS the integration layer between MAF and our domain. Splitting it would add a project for minimal benefit at this scale.

---

## 2. Elegance

**Assessment: GOOD**

The primitive/scope/transport model is clean and validated:
- **Task** is the primitive — one concept to understand, one table everything hangs off
- **Project** is the scope — every entity FK'd to it, cascading deletes
- **Session** is transport — not competing with Task

**One complexity:** Four "workflow" concepts coexist — `WorkflowDefinition` (graph), `WorkflowInterpreter` (runtime), Durable Task (durability), MAF WorkflowBuilder (agent graph). This is inherent to the hybrid approach (ADR-001) and documented, but needs clear naming in code to avoid confusion.

---

## 3. Robustness

**Assessment: GOOD, with known implementation issues to track**

Robustness mechanisms in place:
- Durable Task survives pod restarts (ADR-001)
- Budget enforcement fails fast, never degrades (ADR-009)
- Hash chain detects audit tampering (ADR-008)
- Optimistic concurrency on all entities via `xmin` (ADR-004)
- Idempotency addressed in every ADR (Principle 18)
- Learnings default to `pending` — human gate prevents poisoning (ADR-014)

**Open items from adversarial review (track as implementation TODOs):**

| # | Issue | Severity | Fix |
|---|-------|----------|-----|
| AR-1 | ~~Audit `Channel<T>` with `DropOldest` can lose records~~ | ~~Critical~~ **RESOLVED** | Switched to `BoundedChannelFullMode.Wait` with 5s timeout. `AuditBackpressureException` halts agent execution if channel is saturated — no silent record loss. Updated in ADR-008, walkthrough, solution architecture, and Principle 19 table. |
| AR-2 | ~~Hash chain `_seq` and `_prev` lost on pod restart~~ | ~~Critical~~ **RESOLVED** | Hash chain state (`seq` + `prevHash`) persisted per-stream in Redis via atomic Lua scripts. `SignAsync` throws if Redis is unavailable — agent execution halts. Updated in ADR-008, walkthrough, and solution architecture. |
| AR-3 | Prompt injection — no input sanitisation on injected context | Critical | Escape XML-like tags, add injection detection middleware |
| AR-4 | PCD agent-writable paths lack enforceable allowlist | Critical | Implement path allowlist at service layer, not just prompt instruction |
| AR-5 | No PII detection before content reaches Claude via Foundry | Critical | Pre-flight PII scan (Azure AI Language or Presidio) with hard block |
| AR-6 | AsyncLocal ProjectContext won't propagate into Durable Task activities | High | Pass ProjectContext explicitly as activity input |
| AR-7 | No DR runbook or RTO/RPO targets | High | Define targets, document failover procedure, schedule drills |
| AR-8 | No rate limiting on agent tool calls (only on LLM calls) | High | Per-execution tool call counter with configurable ceiling |
| AR-9 | EF Core 9 EOL November 2026 | High | Target EF Core 10 from the start |
| AR-10 | API key rotation mechanism not fully designed | High | Implement rotation endpoint with 7-day grace period |
| AR-11 | JSONL file logging on ephemeral ACA filesystem | Medium | Mount Azure Files share or remove JSONL safety-net claim |

---

## 4. Efficiency

**Assessment: SLIGHTLY OVER-ENGINEERED in two areas**

### Recommendation 1: Defer Knowledge Graph (ADR-015) to Phase 2

Apache AGE adds a graph database layer for dependency chains and compliance traceability. At v1 scale, pgvector semantic search + relational joins can handle knowledge query needs. The ADR itself marks it as "PoC required."

**Action:** Do not implement ADR-015 in Phase 1. Ship v1 with pgvector + relational queries. If knowledge queries prove insufficient, add AGE in Phase 2. The architecture already specifies graceful degradation: "if AGE proves problematic, the platform functions fully on pgvector alone."

### Recommendation 2: Defer Office Document Generation to Phase 2

The architecture specifies `DocumentFormat.OpenXml`, `ClosedXML`, `QuestPDF` for generating PPTX, DOCX, XLSX from agent output. For v1, agents can produce markdown reports rendered as HTML in the UI — this covers 90% of use cases.

**Action:** Ship v1 with markdown artifacts only. Add Office format generation as Phase 2 tools when there's real user demand.

---

## 5. Reusability

**Assessment: GOOD**

Plugin architecture ensures reusability across all dimensions:
- **Agents** are catalog entries — reusable across projects
- **Templates** define reusable team compositions with inheritance
- **Workflows** are reusable definitions (project-scoped or platform-wide)
- **Tools** are `AIFunction` or MCP — provider-agnostic
- **Learnings** are promotable from project → platform (cross-project knowledge reuse)
- **Wrapper interfaces** (`IAgentRuntime`, `IWorkflowEngine`, `IKnowledgeStore`) ensure the core is swappable (Principle 4)

No reusability concerns.

---

## 6. Duplication

**Assessment: ONE duplication identified**

### Recommendation 3: Remove Polymorphic Embedding Table

The schema has both:
- Inline `Embedding vector(1536)` columns on `ProjectLearning` and `DocumentChunk`
- A separate polymorphic `Embedding` table with `source_table` + `source_id`

The polymorphic table is a prototype holdover. Since all entities that need embeddings have inline vector columns, it's redundant.

**Action:** Remove the `Embedding` entity from the schema. If a future entity needs embeddings but can't have an inline column, add the polymorphic table then.

**Two non-duplications verified:**
- `ProjectEvent` table vs DTS dashboard events — different granularity (domain vs workflow), different audience. Keep both.
- MAF `AgentSession` vs our `Session` entity — runtime vs persistent storage. Principle 4 (Wrap the Core) in action. Keep both.

---

## 7. Consistency

**Assessment: GOOD, with minor items**

| Item | Status | Notes |
|------|--------|-------|
| Entity naming | Consistent | `Project{X}` pattern; `AgenticTask` avoids C# keyword collision |
| JSON column mapping | Document convention | `ToJson()` for typed structures, `[Column(TypeName = "jsonb")]` for flexible payloads |
| Cascading deletes | Consistent | Hard cascade for ownership, SetNull for associations, Restrict for catalog refs |
| Principle compliance | Consistent | All 15 ADRs have `### Principle Compliance` sections |
| Terminology | Consistent | Project/Task/Session/Template used uniformly across all docs |
| Auth level labels | Consistent | `Viewer+`, `Operator+`, `Reviewer+`, `Owner`, `PlatformAdmin` in API design |

---

## 8. Gaps to Fill Before Coding

| # | Gap | Impact | When |
|---|-----|--------|------|
| G-1 | **Implementation plan** | Can't start building without phased plan | **Now** |
| G-2 | **Adversarial review TODOs** (11 items above) | 5 criticals must be addressed in Phase 1 | Phase 1 design |
| G-3 | **Agent prompt library** | 15+ agent system prompts need to be written | Phase 1 |
| G-4 | **CLI design** (`awp` commands) | Operators need CLI from day 1 | Phase 1 |
| G-5 | **Operational runbooks** (DR, incident response) | Required before production | Pre-production |
| G-6 | **Performance/load testing plan** | Need benchmarks | Phase 2 |

---

## 9. Summary of Recommendations

### Simplify for v1 (reduce scope)

| # | Recommendation | Saves |
|---|---------------|-------|
| R-1 | Defer ADR-015 Knowledge Graph to Phase 2 | ~2 weeks implementation, significant operational complexity |
| R-2 | Defer Office document generation (PPTX/DOCX/XLSX) to Phase 2 | ~1 week, 3 NuGet dependencies |
| R-3 | Remove polymorphic `Embedding` table from schema | Eliminates duplication, simplifies schema by 1 entity |

### Fix before production (from adversarial review)

| Priority | Items | Count |
|----------|-------|-------|
| Critical (must fix in Phase 1) | AR-1 through AR-5 | 5 |
| High (should fix in Phase 1) | AR-6 through AR-11 | 6 |

### Fill gaps

| Priority | Items | Count |
|----------|-------|-------|
| Now (blocks coding) | G-1: Implementation plan | 1 |
| Phase 1 | G-2 through G-4 | 3 |
| Pre-production | G-5 through G-6 | 2 |

---

## 10. Final Verdict

**The architecture is ready for implementation.** It is modular (7 projects, clean dependency graph), elegant (Task primitive, Project scope), robust (Durable Task, idempotency, audit hash chain), and well-principled (21 principles applied across 15 ADRs).

The three simplifications (R-1, R-2, R-3) reduce Phase 1 scope without sacrificing the core value proposition. The adversarial review findings are implementation issues, not architectural flaws — the architecture supports fixing them.

**Next step:** Implementation plan.
