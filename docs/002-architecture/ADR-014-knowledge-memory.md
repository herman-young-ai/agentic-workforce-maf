# ADR-014: Knowledge and Memory Management

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team

---

## Context

The Agentic Workforce Platform must accumulate intelligence over time. Agents need to learn from previous executions, avoid repeating mistakes, build on human direction, and share proven patterns across projects. Without persistent knowledge, every execution starts from zero.

## Decision

**Five-type knowledge taxonomy with a read-write cycle per agent task, deduplication via vector similarity, human control over retraction/editing, and platform-wide promotion for proven patterns.**

---

## Knowledge Taxonomy

### 1. Project Context Document (PCD) — "What this project is"

The living document that accumulates everything about the project. Structured JSON with defined sections:

| Section | What It Holds | Who Writes | Trimmed? |
|---------|--------------|-----------|----------|
| `identity` | Project name, objective, tech stack, repo structure | Human (creation) + agents (discovery) | Never |
| `architecture` | Components, data flow, conventions, dependencies | Agents (from code analysis) | Never |
| `principles` | Human-directed rules: "always use repository pattern" | **Humans only** | Never |
| `guardrails` | Hard constraints agents must respect | Humans + template inheritance | Never |
| `current_state` | Active workstreams, priorities, known issues, blockers | Agents (after each execution) | Summarised if large |
| `decisions` | Accumulated architectural/design choices with rationale | Agents (proposed) + humans (approved) | Summarised if large |

**Principles vs guardrails:** Principles are positive direction ("do X"). Guardrails are negative constraints ("never do Y"). Both are human-authored and never trimmed from context — they're instructions, not optional context.

### 2. Learnings — "What we've discovered"

Patterns extracted from agent execution. Discovered, not directed.

| Field | Type | Purpose |
|-------|------|---------|
| `kind` | string | `failure_pattern`, `success_pattern`, `anti_pattern`, `retry_strategy`, `capability_gap`, `domain_insight` |
| `title` | string | Short description: "Auth null refs from missing DI registration" |
| `body` | text | Detailed explanation |
| `recommendation` | text | What to do about it |
| `confidence` | decimal(0-1) | Starts at 0.5, grows with confirmation |
| `occurrence_count` | int | How many times observed |
| `evidence` | JSON | Execution IDs and agent outputs that prove this |
| `agent_names` | string[] | Which agents discovered this |
| `domain_tags` | string[] | For context assembly filtering |
| `status` | string | `pending`, `active`, `retracted`, `superseded` |
| `retracted_by` | string? | Who retracted (human ID) |
| `retracted_reason` | string? | Why it was wrong |
| `superseded_by` | UUID? | If replaced by a corrected learning |
| `contradicts_id` | UUID? | If it contradicts another learning |
| `platform_promoted` | bool | Visible across all projects |
| `promoted_by` | string? | Platform Admin who approved |
| `embedding` | vector(1536) | For deduplication and semantic search |

### 3. Decisions — "What we chose and why"

Explicit choices with rationale and lifecycle:

| Field | Purpose |
|-------|---------|
| `decision` | "Use JWT for internal API auth" |
| `rationale` | "Simpler than OAuth, all consumers are internal" |
| `domain` | "authentication", "architecture", "tooling" |
| `made_by` | Agent name or human ID |
| `status` | `active`, `superseded`, `reversed` |
| `superseded_by` | Link to the replacement decision |

### 4. Intent — "What we're trying to achieve"

Versioned understanding of the objective. Each revision links to its predecessor via `revised_from`:

```
v1: "Scan the payments module for security vulnerabilities"
v2: "Focus on OWASP Top 10, specifically injection and auth bypass"
v3: "Achieve full OWASP compliance including remediation of all critical findings"
```

### 5. Platform Knowledge — "What we know across all projects"

Learnings promoted from project-level to platform-level. Read-only from the project perspective.

---

## The Read-Write Cycle

### Before Each Agent Task (Context Assembly)

The `ProjectContextProvider` assembles knowledge into the agent's context:

```csharp
// In ProjectContextProvider.InvokingAsync()

// 1. PCD — always included, never trimmed
var pcd = await _pcdRepo.LoadAsync(projectId);
messages.Add(SystemMsg($"<pcd>{pcd.ToJson()}</pcd>"));

// 2. Platform learnings (promoted, matching domain tags)
var platformLearnings = await _learningRepo.GetPlatformLearningsAsync(domainTags, max: 3);
if (platformLearnings.Any())
    messages.Add(SystemMsg($"<platform-knowledge>{Format(platformLearnings)}</platform-knowledge>"));

// 3. Project learnings (active, matching domain tags, sorted by confidence)
var projectLearnings = await _learningRepo.GetActiveAsync(projectId, domainTags, max: 5);
if (projectLearnings.Any())
    messages.Add(SystemMsg($"<project-learnings>{Format(projectLearnings)}</project-learnings>"));

// 4. Active decisions (relevant domain)
var decisions = await _decisionRepo.GetActiveAsync(projectId, domain);
if (decisions.Any())
    messages.Add(SystemMsg($"<decisions>{Format(decisions)}</decisions>"));

// 5. For research/reporting projects: existing findings to avoid repetition
if (projectType == "research")
{
    var existing = await _learningRepo.GetAllAsync(projectId, kind: "domain_insight");
    messages.Add(SystemMsg(
        $"<existing-knowledge>You have already reported these findings in previous runs:\n" +
        $"{Format(existing)}\n" +
        $"Do NOT repeat these. Report only genuinely new information.</existing-knowledge>"));
}
```

### After Each Agent Task (Knowledge Extraction)

A `KnowledgeExtractor` processes every agent output:

```csharp
public class KnowledgeExtractor
{
    public async Task ExtractAsync(TaskExecution execution, Guid projectId)
    {
        // 1. Use a lightweight agent to extract learnings from the output
        var candidates = await _extractionAgent.RunAsync<LearningCandidates>(
            $"""
            Analyze this agent execution output and extract reusable learnings.
            For each learning, classify as: failure_pattern, success_pattern,
            anti_pattern, retry_strategy, capability_gap, or domain_insight.
            Only extract genuine insights — not trivial observations.

            Agent: {execution.AgentName}
            Task: {execution.TaskId}
            Status: {execution.Status}
            Output: {execution.OutputData}
            """);

        foreach (var candidate in candidates.Items)
        {
            await DeduplicateAndStore(candidate, execution, projectId);
        }

        // 2. Extract PCD updates (validated, restricted paths protected)
        var pcdUpdates = await _extractionAgent.RunAsync<PcdUpdates>(
            $"Based on this execution, what should be updated in the project context?");
        if (pcdUpdates.HasChanges)
            await _pcdService.ApplyUpdatesAsync(projectId, pcdUpdates);

        // 3. Extract graph entities and relationships (ADR-015)
        // Conditional: only for tasks with relevant domain tags
        // Extracts components, dependencies, findings, OWASP mappings, CVEs
        // into Apache AGE knowledge graph alongside pgvector
        if (ShouldExtractGraph(execution))
        {
            var graphUpdates = await _graphExtractionAgent.RunAsync<GraphExtractionResult>(...);
            await _graphService.ApplyExtractionsAsync(projectId, execution.TaskId, graphUpdates);
        }
    }

    private async Task DeduplicateAndStore(
        LearningCandidate candidate, TaskExecution execution, Guid projectId)
    {
        // Generate embedding for deduplication
        var embedding = await _embeddingClient.GenerateAsync(
            candidate.Title + " " + candidate.Body);

        // Check for existing similar ACTIVE learning (cosine similarity > 0.92)
        // Only reinforce active learnings — pending/retracted/superseded are excluded
        var similar = await _learningRepo.FindSimilarAsync(
            projectId, embedding, threshold: 0.92m, statusFilter: "active");

        if (similar is not null)
        {
            // Already known and human-approved — reinforce without re-approval
            similar.OccurrenceCount++;
            similar.Confidence = Math.Min(1.0m, similar.Confidence + 0.05m);
            similar.Evidence.Add(execution.Id);
            await _learningRepo.UpdateAsync(similar);
        }
        else
        {
            // New learning
            await _learningRepo.CreateAsync(new ProjectLearning
            {
                ProjectId = projectId,
                Kind = candidate.Kind,
                Title = candidate.Title,
                Body = candidate.Body,
                Recommendation = candidate.Recommendation,
                Confidence = 0.5m,
                OccurrenceCount = 1,
                Evidence = new[] { execution.Id },
                AgentNames = new[] { execution.AgentName },
                DomainTags = candidate.DomainTags,
                Embedding = new Vector(embedding),
                Status = "pending"  // Requires human promotion to "active" (R17 security control C16)
            });
        }
    }
}
```

### Deduplication Thresholds

| Threshold | Value | Purpose |
|-----------|-------|---------|
| Duplicate similarity | ≥ 0.92 cosine | Same learning, reinforce existing |
| Contradiction detection | ≥ 0.85 cosine + opposite sentiment | Flag for human review |
| Promotion confidence | ≥ 0.70 | Eligible for platform promotion |
| Promotion min projects | ≥ 2 | Must be confirmed across multiple projects |
| Context assembly max | 5 entries | Max learnings injected per agent turn |

---

## Human Control

### Human Direction vs Agent Discovery

| | Human Direction | Agent Discovery |
|---|---|---|
| **Where** | PCD `principles` and `guardrails` | `ProjectLearning` entities |
| **How created** | Human types explicitly | Agent extracts from execution results |
| **Authority** | Absolute — agents must follow | Advisory — agents should consider |
| **Editable by** | Owner (direction is intentional) | Owner can retract/edit (discovery may be wrong) |
| **Example** | "Always use repository pattern" | "Repository pattern reduced bugs by 30% in this codebase" |

### Permissions

| Action | Owner | Operator | Reviewer | Viewer | Platform Admin |
|--------|-------|----------|----------|--------|----------------|
| View all knowledge (incl. pending) | Yes | Yes | Yes | Yes | Yes |
| Promote pending learning to active | Yes | Yes | No | No | Yes |
| Reject pending learning | Yes | Yes | No | No | Yes |
| Add principles/direction to PCD | Yes | Yes | No | No | Yes |
| Retract a learning (mark as wrong) | Yes | Yes | No | No | Yes |
| Edit a learning | Yes | No | No | No | Yes |
| Supersede a learning (create corrected version) | Yes | No | No | No | Yes |
| Propose promotion to platform | Yes | No | No | No | Yes |
| Approve promotion to platform | No | No | No | No | Yes |
| Edit/retract platform learnings | No | No | No | No | Yes |
| Purge retracted learnings (permanent delete) | No | No | No | No | Yes |

### Pending → Active Promotion (Security Gate)

Agent-extracted learnings start in `pending` status. They are **excluded from context assembly** until a human promotes them to `active`. This prevents learning poisoning attacks where an agent extracts a malicious "learning" that shapes future agent behaviour.

1. Agent extracts candidate learning → stored as `pending`
2. `pending` learning surfaces in the Knowledge View for the project owner/operator to review
3. Owner/operator promotes to `active` (learning now included in context assembly) or rejects (learning retracted with reason)
4. If unreviewed after 30 days, `pending` learnings auto-expire (status → `retracted`, reason: "auto-expired: unreviewed")

The human gate applies to **all agent-extracted learnings**, not just platform promotion. This is a defence-in-depth control for regulated environments. See [R17 security review §C16](../098-research/R17-response-security-design.md).

> **Note:** Reinforcement of existing `active` learnings (occurrence count + confidence bump via deduplication) does **not** require re-approval — the learning was already human-approved.

### Retraction, Not Deletion

When a human says "that's wrong, forget it":

1. Learning status → `retracted`
2. `retracted_by` and `retracted_reason` recorded
3. Learning **excluded from context assembly** — agents no longer see it
4. Learning **remains visible** in Knowledge View (greyed out) — audit trail preserved
5. Only Platform Admin can permanently purge (for GDPR/data retention compliance)

If the human provides a correction:
1. Old learning → `superseded`
2. New corrected learning created with `superseded_by` link
3. Both visible in Knowledge View — full history of what was learned and corrected

---

## Platform Knowledge Promotion

```
Project-level learning
    │ confidence ≥ 0.7 AND occurrence_count ≥ 2
    ▼
Owner proposes promotion → flagged for review
    │
    ▼
Platform Admin reviews:
    ├── Is this generally applicable (not project-specific)?
    ├── Confirmed independently across ≥ 2 projects?
    └── Any contradicting evidence?
    │
    ▼
Approved → platform_promoted = true
    │
    ▼
All future projects see this learning automatically
(injected by ContextAssembler when domain tags match)
```

Platform learnings are **read-only** from the project perspective. Only Platform Admins can edit, retract, or demote them.

---

## Knowledge View (UI)

### Per-Project Knowledge Tab

```
┌─────────────────────────────────────────────────────────────┐
│ Project: Payments Security Q2              [Knowledge]       │
├──────┬────────────┬───────────┬────────────┬────────────────┤
│ PCD  │  Learnings │ Decisions │ Intent     │ Documents      │
├──────┴────────────┴───────────┴────────────┴────────────────┤
│                                                              │
│ 🌐 [platform] "Git blame context improves code reviews"     │
│    Promoted | Confidence: 0.95 | Confirmed across 4 projects │
│                                                              │
│ ✅ [0.95] "Don't use string concat for SQL in payments"     │
│    anti_pattern | Seen 4× | By: security.reviewer            │
│    Evidence: [exec-001] [exec-007] [exec-012] [exec-015]    │
│    [Retract] [Edit] [Propose promotion]                      │
│                                                              │
│ ✅ [0.85] "Auth null refs from missing DI registration"     │
│    failure_pattern | Seen 2× | By: coder                     │
│    [Retract] [Edit]                                          │
│                                                              │
│ ⚠️ [0.50] "Payments API may use XML not JSON"               │
│    domain_insight | Seen 1× | By: researcher                 │
│    [Retract] [Edit]                                          │
│                                                              │
│ 🚫 [retracted] "Settlement API uses SOAP"                   │
│    Retracted by: Herman | "Actually uses REST + ISO 20022"  │
│    Superseded by: "Settlement API uses REST + ISO 20022"     │
│                                                              │
│ Filter: [All kinds ▼] [All agents ▼] [Min confidence ▼]     │
│         [Show retracted ☑]                                   │
└──────────────────────────────────────────────────────────────┘
```

### PCD Principles Tab

```
┌─────────────────────────────────────────────────────────────┐
│ PCD > Principles                            [+ Add]          │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│ 📌 "Always use async/await in this codebase"                │
│    Added by: Herman | May 8                                  │
│    [Edit] [Remove]                                           │
│                                                              │
│ 📌 "Use repository pattern for all data access"             │
│    Added by: Herman | May 7                                  │
│    [Edit] [Remove]                                           │
│                                                              │
│ 📌 "All API endpoints must validate input with FluentVal"   │
│    Added by: Thabiso | May 9                                 │
│    [Edit] [Remove]                                           │
│                                                              │
│ 🔒 "Never store secrets in configuration files" [guardrail] │
│    Inherited from template | Cannot be removed               │
└──────────────────────────────────────────────────────────────┘
```

---

## Context Assembly Priority (Updated)

| Priority | Layer | Source | Max Tokens | Trimmed? |
|----------|-------|--------|-----------|----------|
| 0 | PCD (identity, principles, guardrails) | DB | — | Never |
| 0a | Task definition | Execution plan | — | Never |
| 1 | Upstream task inputs | Previous task outputs | Variable | Summarised if over budget |
| 2 | Platform learnings (promoted, domain-matched) | DB (platform) | ~500 | Skipped if budget tight |
| 3 | Project learnings (active, domain-matched, by confidence) | DB (project) | ~1000 | Skipped if budget tight |
| 4 | Active decisions (relevant domain) | DB | ~500 | Skipped if budget tight |
| 4a | Graph context (dependency/compliance chains) | AGE traversal | ~800 | Skipped if budget tight |
| 5 | Existing findings (research projects — dedup context) | DB | ~1000 | Trimmed |
| 6 | Uploaded document chunks (semantically relevant) | pgvector search | ~2000 | Trimmed |
| 7 | Code Map | Generated | Only coding tasks | Token-capped |
| 8 (lowest) | Execution history (recent failures) | DB | Variable | Trimmed first |

**Human direction (PCD principles and guardrails) is never trimmed.** It's at priority 0 with the PCD itself.

---

## The Knowledge Extraction Agent

A lightweight agent (Claude Haiku) that runs after every task execution to extract learnings. It's called by the `KnowledgeExtractor` service, not by the workflow directly.

**Instructions:**
```
You are a knowledge extraction specialist. Analyze the execution output and extract
reusable learnings. For each learning:

1. Classify: failure_pattern, success_pattern, anti_pattern, retry_strategy,
   capability_gap, or domain_insight
2. Write a concise title (one sentence)
3. Explain the learning in detail
4. Provide an actionable recommendation
5. Assign domain tags for filtering

Rules:
- Only extract genuine insights — not trivial observations
- If the task failed, extract what went wrong and how to avoid it
- If the task succeeded, extract what worked well and why
- Do not extract learnings that are just restating the task definition
- Maximum 3 learnings per execution (quality over quantity)
```

**Cost:** Runs on Haiku (~$0.001 per extraction). Factored into the project budget automatically.

---

## Consequences

- Knowledge extraction adds a small cost per execution (Haiku call for extraction + embedding generation)
- Deduplication requires vector similarity search on every new learning — cached and batched for efficiency
- Retracted learnings preserve audit trail but add visual noise — UI filters help
- Platform promotion requires Platform Admin approval — potential bottleneck at scale
- PCD `principles` section is human-only write — agents cannot modify human direction
- Research projects with many learnings (hundreds) need efficient context assembly — top-N by confidence + domain tag filtering
- Knowledge extraction agent must be tuned to avoid extracting trivial observations — prompt engineering and feedback loop needed
- `KnowledgeExtractor` runs asynchronously after task completion — does not block the execution pipeline
- Graph entity extraction (step 3, ADR-015) adds a second Haiku call for relevant tasks (~$0.001) — see [ADR-015: Knowledge Graph](ADR-015-knowledge-graph.md) for schema, tools, and integration details

### Principle Compliance

- **P14 Secure by Default:** Learnings default to `pending` (already implemented). Additionally, the extraction agent has no write access to PCD `principles` or `guardrails` paths — enforced at the service layer, not just prompt-level instruction.
- **P15 Backend Owns All Logic:** All knowledge promotion, retraction, deduplication, and context assembly logic runs server-side. The Knowledge View UI is display + action triggers only — no client-side filtering that could bypass permission checks.
- **P16 Single Source of Truth:** Each knowledge type has exactly one authoritative write path. Learnings are written only by `KnowledgeExtractor` and humans — no other service creates learning records. PCD is written only by the `PcdService`.
- **P18 Idempotency:** If `KnowledgeExtractor` runs twice for the same execution (retry after failure), it does not create duplicate learnings. An execution-level idempotency key prevents re-extraction entirely, beyond the cosine deduplication check.
- **P19 Bounded Resource Usage:** Explicit limits: max learnings per project, max pending learnings before extraction pauses, max embedding generation calls per minute, and a timeout on the extraction agent call.
- **P20 Version Everything:** PCD JSON structure and learning record schema are versioned via `format_version`. Migrations are performed transparently when fields are added/removed.
- **P21 Explicit Over Implicit:** Knowledge extraction rules ("only genuine insights," "maximum 3 per execution") are enforced as explicit server-side constraints (max items validated before DB write), not just hoped-for from the LLM prompt.
