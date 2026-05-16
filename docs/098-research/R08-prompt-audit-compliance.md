# R08: Audit, Compliance, and Immutable Evidence

## Prompt for claude.ai

---

I am building an AI agent orchestration platform in C# on Azure for a regulated bank (Investec — dual-regulated by SARB/PA in South Africa and FCA/PRA in UK). Every agent action must be auditable, and evidence of agent decisions must be immutable for compliance.

Give me a **concise architecture** for the audit and evidence subsystem. Practical patterns — not compliance theory.

### What Must Be Audited

**LLM call audit (every inference call):**

| Field | Example |
|---|---|
| timestamp | 2026-05-10T14:32:01Z |
| agent_name | security.reviewer |
| model | claude-sonnet-4 |
| provider | foundry-anthropic |
| mission_id | uuid |
| execution_id | uuid |
| input_tokens | 12400 |
| output_tokens | 3200 |
| cache_read_tokens | 8000 |
| cost_usd | 0.0234 |
| latency_ms | 4521 |
| tool_calls | ["file_read", "web_search"] |
| content_hash_input | sha256 of input |
| content_hash_output | sha256 of output |

Volume: ~1000-10000 calls/day during active use.

**Agent action audit (every tool invocation):**

| Field | Example |
|---|---|
| timestamp | 2026-05-10T14:32:05Z |
| agent_name | coder |
| action_type | file_write |
| target | src/auth/login.cs |
| mission_id | uuid |
| execution_id | uuid |
| input_summary | "Write authentication middleware" |
| output_summary | "Created file, 42 lines" |
| approval_required | false |
| approved_by | null |

Volume: ~500-5000 actions/day.

**Human decision audit (every approval/rejection):**

| Field | Example |
|---|---|
| timestamp | 2026-05-10T15:00:00Z |
| decision_type | plan_approval |
| mission_id | uuid |
| user_id | uuid |
| decision | approved |
| rationale | "Plan looks reasonable" |
| workflow_run_id | uuid |

Volume: ~10-100/day.

### Compliance Requirements

1. **Immutability** — audit records must not be modifiable after creation (WORM — Write Once Read Many)
2. **Retention** — minimum 7 years for regulated activities (FCA/SARB requirement)
3. **Searchability** — compliance team must be able to search/filter audit records by date, agent, mission, action type
4. **Evidence chain** — for any agent output, we must be able to trace back to: the exact prompt, model version, tool calls, and human approvals that produced it
5. **Data residency** — South Africa data stays in South Africa North region; UK data stays in UK South
6. **Export** — audit records must be exportable for regulatory examination
7. **Tamper detection** — hash chain or equivalent to detect if records have been altered

### Azure Services to Evaluate

| Service | Use Case | Question |
|---|---|---|
| Azure SQL / PostgreSQL | Primary audit table | Sufficient for immutability? Or do we need WORM? |
| Azure Blob Storage (immutable) | Evidence files (full prompts, responses) | WORM policies — how to configure? |
| Azure Event Hubs | Real-time audit event stream | Capture + forward pattern? |
| Azure Data Explorer (ADX) | Long-term audit analytics | Cost-effective for 7-year retention + search? |
| Azure Monitor / App Insights | Operational audit | Overlap with custom audit? |
| Azure Purview | Data governance | Relevant for agent data lineage? |

### Questions to Answer

1. **Architecture pattern**: What's the recommended audit pipeline? (e.g., app → Event Hub → ADX for analytics + Blob for WORM evidence?)
2. **Immutable Blob Storage**: Show the pattern for writing an evidence file (full LLM input/output) to immutable blob storage with a time-based retention policy.
3. **Hash chain**: How to implement a simple hash chain for tamper detection on audit records (each record includes hash of previous record)?
4. **EF Core integration**: How to implement audit logging as MAF middleware (IChatClient middleware for LLM calls, function calling middleware for tool invocations) that writes to the audit pipeline without blocking agent execution?
5. **Cost**: Rough monthly cost estimate for storing 7 years of audit data at our volume (10k LLM calls/day, 5k actions/day).
6. **Regulatory**: Any Azure-specific certifications or features that help satisfy FCA/SARB requirements for AI system audit trails?

### Output Format

- **Architecture diagram** (text/ASCII): show the audit data flow from agent action → pipeline → storage
- Answer each question concisely
- **Recommendation**: primary audit architecture with specific Azure services
- **Code sketch**: MAF middleware that captures LLM call audit data (10-15 lines C#)

Keep total response under 2000 words.

---

## After Research

Save claude.ai's response as: `docs/098-research/R08-response-audit-compliance.md`
