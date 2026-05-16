# R17a: AI Security Control Framework Alignment Assessment

**Date:** 2026-05-11
**Input:** [AI Security Control Framework](../../../ai-security-control-framework/) (9 categories, 105 risks, 11 themes, 48 objectives)
**Against:** [R17 Security Architecture](R17-response-security-design.md) (40 controls, 9 areas)

---

## Executive Summary

The AI Security Control Framework is the **organisation-wide** AI risk governance framework (board-level, 9 risk categories, 105 risks). R17 is the **platform-specific** security architecture for the Agentic Workforce Platform. They operate at different levels but must be aligned.

**Key finding:** R17 covers the _platform engineering_ controls well but is missing the _governance wrapper_ the framework requires. The framework mandates process gates, registry entries, tier classification, and measurable outcomes that R17 doesn't address because R17 is an architecture doc, not a governance doc.

**Alignment score:** R17 directly addresses controls for **28 of 48** framework objectives. 11 objectives are partially addressed. 5 are not addressed and relevant. 4 are not applicable (enterprise-wide scope only).

---

## Detailed Alignment: Framework Objectives vs R17 Controls

### Theme 1: AI Architecture & Risk Review Gate

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **1.1** AI risk classification (4-tier + 5 modifiers) | Every AI system classified before development. Tier determines governance. | **Not addressed** | R17 does not classify the AWP against the 4-tier model. The AWP is clearly **Tier 4** (open-ended multi-step agentic, dynamic tool selection, sub-agent spawning). All 5 modifiers apply: trust-laundering (agent output feeds execution sinks), blast-radius (cross-project, multi-tenant), aggregate blast-radius (many tasks × many projects), regulated-omission (if used for sanctions/AML), decision-influence (reports used by regulators). **Must document.** |
| **1.2** Lethal trifecta test | Three-property test: private data + untrusted content + external actions. If all three → executive risk acceptance. | **Not addressed** | The AWP has all three trifecta properties: (1) accesses client documents and PII, (2) processes uploaded documents and user inputs, (3) executes shell commands, calls APIs, writes files. R17 has compensating controls (sandbox isolation, HITL gates, content safety) but does not document the trifecta assessment or executive risk acceptance. **Must document.** |
| **1.3** Trust boundary and data flow validation | End-to-end data flow mapped. Vector ACL parity. Trust-laundering test. | **Partial** | R17 Area 3 §6 covers cross-project isolation (project_id partitioning, vector store filtering). But does not document the full data flow diagram, does not explicitly test vector ACL parity (pgvector ACLs vs source-system permissions), and does not apply the trust-laundering test to the PCD → agent → tool execution chain. |
| **1.4** Agent scope and permission review | Enumerated tools, blast-radius, aggregate exposure, credential scope. | **Covered** | R17 C20 (tool manifest per agent), C17 (PCD path allowlist), C22 (guardrail enforcement), C19 (sandbox isolation). The permission scoping is well-designed. Missing: blast-radius calculation per-action and aggregate, regulated-omission check. |
| **1.5** Pre-production sign-off gate | Dual sign-off (CTO line + CISO/CRO line). | **Not addressed** | R17 does not define a production deployment gate. ADR-012 defines the pipeline stages but no security sign-off gate. The framework requires dual independent challenger for Tier 3-4 systems. **Must add to deployment pipeline.** |

### Theme 2: AI Asset Discovery & Inventory

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **2.1** Continuous AI discovery | Detect AI usage within 48 hours. | **N/A** | Enterprise-wide scope. The AWP is a _sanctioned_ platform — it IS the governed alternative to shadow AI. |
| **2.2** AI model and system registry | Central inventory with tier, owner, data sources, status. | **Partial** | R17 C35 (model version pinning + MRM) and Area 9 §MRM Integration cover registry fields. But the AWP itself and each deployed agent template need to be registered in the Group AI registry (ServiceNow CMDB per framework). Not just the models — the platform, templates, and workflows. |
| **2.3** Shadow agent detection | Response process for unregistered AI usage. | **Covered (by design)** | The AWP is the answer to risk 4-06 (shadow agent deployment). All agent execution is centrally governed, audited, and observable. The AWP should be positioned as the Group's sanctioned agentic platform, making shadow agent use unnecessary. |
| **2.4** EU AI Act Article 5 screening | Prohibited practice assessment. | **Not addressed** | R17 does not include an Article 5 screening. The AWP is unlikely to perform prohibited practices, but a documented assessment is required by the framework. **Quick win — checklist.** |

### Theme 3: Vendor AI Due Diligence & Monitoring

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **3.1** AI-specific vendor risk assessment | Training data provenance, model change notification, IP indemnification. | **Partial** | R17 C35 (model version pinning) and Area 9 §Third-Party Risk cover service disruption, data handling, and model behaviour change. But does not cover: training data provenance assessment, IP indemnification status, or contractual model change notification lead times for Anthropic/Microsoft. |
| **3.2** Continuous vendor AI performance monitoring | Detect silent model updates, false-negative drift, output distribution shifts. | **Partial** | R17 C35 (regression test on model update) is the right approach. But does not define: baseline metrics, drift detection thresholds, or independent monitoring (vs vendor-reported). The AWP's audit pipeline (ADR-008) captures the data needed — but no monitoring rules are defined against it. |
| **3.3-3.6** Vendor terms, supply chain, DORA | Contractual monitoring, MLBOM, DORA register of information. | **Not addressed** | Enterprise procurement scope — not platform-specific. The AWP should ensure its Anthropic/Microsoft relationships are included in the Group's DORA register of information (Article 28(3)). |

### Theme 4: Managed AI Gateway & Data Protection

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **4.1** Mandatory AI traffic proxy | 95% coverage of managed endpoints. | **N/A** | Enterprise-wide scope (enterprise browser, NGFW). The AWP uses private endpoints to Azure AI Foundry — traffic is already within the managed perimeter. |
| **4.2** AI-aware DLP | Natural language DLP, MNPI detection, PII across jurisdictions. | **Covered** | R17 C24 (PII detection + redaction via Azure AI Language). Scans agent outputs before storage. Also C25 (data residency enforcement). The AWP's PII detection is ahead of the enterprise-wide gap (which has no AI-aware DLP). |
| **4.3** Information barrier enforcement | Vector ACL parity across wall-crossed functions. | **Partial** | R17 Area 3 §6 (cross-project isolation via project_id partitioning) provides isolation. But does not address information barriers _within_ a project if project members span wall-crossed functions. The AWP should enforce that project membership cannot span Chinese wall populations — or that the PCD/learnings within a project respect information barrier logic. |
| **4.4** AI communications capture and archiving | Copilot chats, AI assistant conversations archived for regulatory recordkeeping. | **Covered** | R17 C33 (hash chain + Merkle root) and ADR-008 (WORM audit pipeline) capture all LLM interactions with 7-year immutable retention. The AWP's audit trail exceeds the framework's requirement. |

### Theme 5: AI Application Security Testing

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **5.1** AI-specific security testing in CI/CD | Garak, Promptfoo, PyRIT. OWASP LLM Top 10 coverage. | **Partial** | R17 Area 8 §4 defines pen test scope (prompt injection, auth bypass, privilege escalation, data exfiltration, session ID prediction). But does not specify automated AI security testing in CI/CD. No mention of Garak, Promptfoo, or PyRIT. Red team exercises are periodic, not continuous. **Should add automated prompt injection testing to the CI/CD pipeline.** |
| **5.2** Multi-turn and indirect prompt injection testing | Crescendomation, banking-document corpus. | **Partial** | R17 C15 (5-layer prompt injection defence) and pen test scope covers multi-turn injection. But no automated testing with banking-specific corpora in CI/CD. |
| **5.3** Adversarial robustness testing for ML classifiers | IBM ART, white-box and black-box. | **N/A (Monitor)** | The AWP consumes API models, does not train classifiers. If fine-tuning is introduced, this becomes relevant. |
| **5.4** AI-generated code security assurance | Slopsquatting detection, CWE rate measurement. | **Partial** | R17 C30 (supply chain security — CodeQL SAST, Defender for DevOps). Covers code scanning but doesn't specifically address AI-generated code quality metrics (CWE rate). The AWP's own codebase may include AI-generated code — CodeQL covers this. Agent-generated code runs in sandbox only (C19) — so CWE rate is mitigated by isolation. |

### Theme 6: Agent Governance, IAM & Observability — **Most Critical Theme**

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **6.1** Agent identity and NHI management | Dedicated NHI per agent, no shared accounts, lifecycle management. | **Covered** | R17 Area 4 §4 (agent identity server-assigned, per-agent tagging with agentName/instanceId/executionId/taskId). ADR-012 (separate UAMI per Container App). Each agent instance has a unique identity. |
| **6.2** Least-privilege enforcement | OPA/Cedar for Tier 4. Enumerated permissions. | **Partial** | R17 C20 (tool manifest per agent — enumerated, immutable), C22 (guardrail enforcement — programmatic interceptor). But R17 acknowledges OPA is a gap (G2). The framework requires OPA/Cedar for Tier 4 systems. **The AWP IS a Tier 4 system.** OPA integration should be prioritised. |
| **6.3** MCP and agent-tool supply chain | Allow-listing, tool-definition hashing, schema inspection. | **Partial** | ADR-011 covers MCP/A2A architecture. R17 C20 (tool manifest immutable, server-set). But does not specify: tool-definition hashing and pinning, schema-level inspection (vs description-level), rug-pull detection. The framework cites 23-41% higher attack success for MCP integrations. **Should add tool-definition hashing to the agent factory.** |
| **6.4** Agent audit trail and decision logging | Tamper-resistant, replayable, BCBS 239 compliant. | **Covered** | R17 C33 (hash chain + Merkle root), ADR-008 (WORM pipeline, 7-year retention, per-agent stream). The AWP's audit trail is the strongest single control in the architecture. Full trace: user → project → execution → task → agent → tool call → audit record. |
| **6.5** Agent memory and context integrity | Provenance tracking, cross-user isolation. | **Covered** | R17 C16 (learning human gate — pending → active), C17 (PCD path allowlist), C13 (cross-project isolation). ADR-014 (knowledge taxonomy with retraction, supersession, and provenance via source_execution_id). Directly addresses risk 4-10 (persistent memory and context poisoning). |
| **6.6** Agent circuit breakers and kill-switches | Automated triggers, pre-approved fallback. | **Covered** | R17 C31 (emergency stop — Redis kill switch + orchestration terminate), C19 (sandbox isolation with network egress disabled). ADR-009 (budget enforcement — fail fast, never degrade). Circuit breakers exist at multiple levels: budget, guardrail violation, content safety violation, emergency stop. |

### Theme 7: AI-Enhanced Threat Detection

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **7.1-7.3** Phishing detection, deepfake detection, SOC AI rules | Enterprise SOC capabilities. | **N/A** | Enterprise-wide SOC scope, not platform-specific. R17 C38 (SIEM/Sentinel integration) ensures AWP alerts flow to the SOC. |

### Theme 8: Identity Verification Hardening

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **8.1** Cryptographic out-of-band verification for high-value actions | FIDO2, signed transaction tokens. Voice/video excluded as identity factors. | **Partial** | R17 C37 (PIM-activated roles with MFA + FIDO2 for privileged operations), C32 (break-glass with FIDO2). But does not address: cryptographic HITL confirmation for agent-initiated high-value actions (payments, trades, data exports). The framework explicitly states voice/video must be excluded as identity factors for high-value actions. **Should define a threshold above which agent-initiated actions require FIDO2 confirmation from the human approver, not just a click-to-approve in the UI.** |
| **8.2-8.3** KYC/voice biometric hardening | Enterprise identity capabilities. | **N/A** | Enterprise-wide scope. |

### Theme 9: Model Lifecycle & Validation Platform

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **9.1** Centralised model registry | 7-state lifecycle, DORA Art. 28. | **Partial** | R17 C35 + Area 9 §MRM Integration defines registry fields. But does not implement the 7-state lifecycle machine (development → validated → approved → production → monitoring → under-review → decommissioned). The AWP's agent catalog is a functional registry but lacks formal lifecycle states. |
| **9.2** Tiered validation and effective challenge | SHAP/LIME, adversarial robustness, fairness. | **N/A (Monitor)** | The AWP consumes API models, does not train. If fine-tuning is introduced, this triggers. |
| **9.3** Continuous performance monitoring and drift | Sub-daily for dynamic ML. | **Partial** | R17 C35 (regression test on version update). ADR-008 audit data enables monitoring. But no automated drift detection rules defined. |
| **9.4** Change control and cumulative-change tracking | Material vs non-material taxonomy. | **Covered** | R17 Area 9 §Change Management (agent prompt changes, model version changes, workflow changes — all with defined approval chains). |
| **9.5** Model documentation and reproducibility | EU AI Act Annex IV. | **Partial** | R17 C33 (full audit trail). ADR-008 (model ID, tokens, input/output hash per call). But does not explicitly produce an EU AI Act Annex IV technical documentation package. |
| **9.6** Decommissioning and kill-switch | Documented procedure. | **Covered** | R17 C31 (emergency stop). API Design §1.29 (emergency endpoints). |

### Theme 10: AI Output Validation, Calibration & Fairness

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **10.1** Hallucination detection and grounding | Finance-specific entity validation. | **Partial** | R17 C15 (L3 — structured output parsing) and C21 (Content Safety). But no finance-specific entity validation (e.g., verifying cited account numbers, regulatory references, or financial figures against source data). The framework requires deterministic cross-checks for numerical outputs. |
| **10.2** Confidence calibration monitoring | Reliability diagrams, Brier scores. | **Not addressed** | R17 does not address model confidence calibration. If agents produce risk scores or recommendations that influence decisions, calibration monitoring is needed. |
| **10.3** Numerical output verification | No LLM-only numbers in consequential decisions. | **Not addressed** | R17 does not explicitly prohibit LLM-generated numerical outputs from feeding directly into financial calculations. The guardrail framework (C22) could enforce this, but no guardrail type for "numerical verification required" exists. **Should add a guardrail type.** |
| **10.4** Continuous fairness monitoring | Quarterly, across acquisition + decisioning + servicing. | **N/A (Monitor)** | Only relevant if the AWP is used for customer-facing decisions (credit, pricing). Not currently in scope. |
| **10.5** Training-serving feature parity | Automated in deployment pipeline. | **N/A** | API consumption only. |

### Theme 11: AI Operational Resilience & Exit Planning

| Objective | Framework Requires | R17 Status | Gap |
|-----------|-------------------|-----------|-----|
| **11.1** AI dependency mapping | Against important business services. | **Partial** | R17 Area 9 §Third-Party Risk acknowledges Anthropic/Microsoft dependencies. But does not map AWP dependencies against the Group's Important Business Services (IBS) register per SS1/21. |
| **11.2** AI provider failure stress testing | Concurrent multi-provider failure. | **Partial** | R17 notes "Fallback to Azure OpenAI. No single-model dependency." But no documented stress test for concurrent Anthropic + Azure OpenAI failure. |
| **11.3** Exit planning and provider portability | Weights, training data, evaluation harnesses. | **Partial** | The AWP consumes API models (no weights to port). But exit planning should cover: can the platform switch to a different provider (e.g., Google Vertex) within defined timeframes? What is the porting cost? |
| **11.4** Manual fallback capability | SS1/21 spirit. | **Not addressed** | If the AWP becomes an Important Business Service, what is the manual fallback? Can the business processes the AWP automates be performed manually if the platform is unavailable? **Should define this.** |

---

## Risk Coverage: Framework Category 4 (Agentic AI) — Direct Applicability

All 14 Category 4 risks apply directly to the AWP. Assessment:

| Risk | Description | R17 Coverage | Assessment |
|------|------------|-------------|-----------|
| **4-01** | Excessive agency / over-permissioned agents | C20 (tool manifest), C22 (guardrails), C17 (PCD path allowlist) | **Covered** |
| **4-02** | Lethal trifecta — structurally exploitable architecture | Compensating controls exist (sandbox, HITL, content safety) but no documented trifecta assessment | **Gap — must document** |
| **4-03** | Recursive/chained prompt injection across agent steps | C15 (5-layer defence), C16 (learning human gate), C21 (Content Safety) | **Covered** |
| **4-04** | MCP and agent-protocol exploitation | ADR-011 (MCP architecture), C20 (tool manifest). Missing: tool-definition hashing, rug-pull detection | **Partial** |
| **4-05** | Zero-click agent scope violations | C19 (Dynamic Sessions Hyper-V isolation), C28 (firewall allowlist), C18 (sandbox file scope) | **Covered** |
| **4-06** | Shadow agent deployment | The AWP IS the answer. Sanctioned, governed, observable. | **Covered (by design)** |
| **4-07** | Agent identity and accountability gaps | C15 Area 4 §4 (server-assigned identity, full trace) | **Covered** |
| **4-08** | Agent-to-agent trust and communication | No inter-agent communication except through Durable Task orchestrator | **Covered** |
| **4-09** | Non-deterministic decision-making and goal drift | C16 (learning human gate), C22 (guardrail enforcement), HITL approval gates | **Partial** — no explicit drift detection over time |
| **4-10** | Persistent memory and context poisoning | C16 (pending → active learning gate), C17 (PCD path allowlist), C13 (cross-project isolation) | **Covered** |
| **4-11** | Agent credential and NHI sprawl | ADR-012 (separate UAMI per Container App), C29 (Key Vault + MI only) | **Covered** |
| **4-12** | Non-replayable agent audit trails | ADR-008 (WORM pipeline, hash chain, 7-year retention) | **Covered** |
| **4-13** | Hallucinated and phantom tool calls | C22 (guardrail enforcement — tool call validated against manifest before execution) | **Covered** |
| **4-14** | Cascading multi-agent failures and deadlocks | C31 (emergency stop), ADR-009 (budget enforcement — fail fast) | **Partial** — no explicit deadlock detection |

---

## Priority Actions

### Must Do (governance gaps the framework requires for Tier 4 systems)

| # | Action | Framework Ref | Effort |
|---|--------|--------------|--------|
| 1 | **Document AWP tier classification**: Tier 4 with all 5 modifiers applied and documented | 1.1 | Low (document) |
| 2 | **Document lethal trifecta assessment**: all three properties present → executive risk acceptance with compensating controls documented | 1.2 | Low (document + exec sign-off) |
| 3 | **Add pre-production sign-off gate** to deployment pipeline: dual sign-off (CTO + CISO line) for the AWP and any template change | 1.5 | Medium (process) |
| 4 | **Register AWP in Group AI registry**: ServiceNow CMDB entry with all mandatory fields (tier, owner, data sources, status, accountable individual) | 2.2 | Low (registry entry) |
| 5 | **Complete EU AI Act Article 5 screening** for the AWP | 2.4 | Low (checklist) |
| 6 | **Define FIDO2 threshold for agent-initiated high-value actions**: approval gates for payments/trades/data exports above defined thresholds must require FIDO2 hardware confirmation, not just UI click | 8.1 | Medium (implementation) |

### Should Do (technical gaps)

| # | Action | Framework Ref | Effort |
|---|--------|--------------|--------|
| 7 | **Add automated AI security testing to CI/CD**: Garak or Promptfoo for prompt injection, run on template/prompt changes | 5.1, 5.2 | High (new capability) |
| 8 | **Add tool-definition hashing** to the agent factory: hash MCP tool definitions at registration, verify at invocation, detect rug-pull | 6.3 | Medium |
| 9 | **Prioritise OPA/Rego integration** for the guardrail enforcement layer — required by framework for Tier 4 | 6.2 | High |
| 10 | **Add numerical output verification guardrail type**: agent outputs containing financial figures must be cross-checked against source data before delivery | 10.1, 10.3 | Medium |
| 11 | **Define manual fallback procedures**: if AWP becomes an IBS, what is the manual alternative? | 11.4 | Low (document) |
| 12 | **Map AWP against Group IBS register**: determine if AWP is or will become an Important Business Service under SS1/21 | 11.1 | Low (assessment) |

### Consider (alignment opportunities)

| # | Action | Framework Ref | Effort |
|---|--------|--------------|--------|
| 13 | Add information barrier enforcement to project membership (prevent wall-crossing within a project) | 4.3 | Medium |
| 14 | Define vendor performance baseline metrics and drift detection rules against audit data | 3.2, 9.3 | Medium |
| 15 | Produce EU AI Act Annex IV technical documentation package from existing audit data | 9.5 | Medium |
| 16 | Stress-test concurrent Anthropic + Azure OpenAI failure scenario | 11.2 | Low |

---

## Alignment Summary

| Framework Theme | Objectives | Covered | Partial | Not Addressed | N/A |
|----------------|-----------|---------|---------|---------------|-----|
| 1. Architecture & Risk Gate | 5 | 1 | 1 | 3 | 0 |
| 2. Discovery & Inventory | 4 | 1 | 1 | 1 | 1 |
| 3. Vendor Due Diligence | 6 | 0 | 2 | 4 | 0 |
| 4. Gateway & Data Protection | 4 | 2 | 1 | 0 | 1 |
| 5. AppSec Testing | 4 | 0 | 3 | 0 | 1 |
| 6. Agent Governance (critical) | 6 | 4 | 2 | 0 | 0 |
| 7. Threat Detection | 3 | 0 | 0 | 0 | 3 |
| 8. Identity Hardening | 3 | 0 | 1 | 0 | 2 |
| 9. Model Lifecycle | 6 | 2 | 3 | 0 | 1 |
| 10. Output Validation | 5 | 0 | 1 | 2 | 2 |
| 11. Operational Resilience | 4 | 0 | 3 | 1 | 0 |
| **Total** | **50** | **10** | **18** | **11** | **11** |

**Bottom line:** R17 is strong on platform engineering controls (agent identity, sandbox isolation, audit trail, data protection, emergency stop) — these are the hardest controls to retrofit. The gaps are primarily governance documentation (tier classification, trifecta assessment, registry entry, Article 5 screening, production sign-off gate) which are low-effort to close. The two substantive technical gaps are OPA/Rego policy engine (already flagged as R17 gap G2) and automated AI security testing in CI/CD.
