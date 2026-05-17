# ADR-018: Workflow Graph Storage — JSON Document, Not Normalized Tables

**Status:** Accepted
**Date:** 2026-05-17
**Decision Makers:** Architecture team
**Supersedes:** Implicit relational model from initial scaffold (WorkflowNode / WorkflowEdge entity tables)
**Refines:** [ADR-013](ADR-013-workflow-design.md) — workflow design
**Related:** [ADR-001](ADR-001-workflow-engine.md), [ADR-004](ADR-004-data-layer.md)

---

## Context

ADR-013 declared that workflow definitions are stored as editable directed graphs. It did not specify the storage shape. The initial scaffold modelled this as two normalized tables — `WorkflowNode` and `WorkflowEdge` — with foreign keys to `WorkflowDefinition`. During Phase 1 domain alignment we changed this to a single `WorkflowDefinition` row with `nodes` and `edges` as `jsonb` columns.

This ADR records that decision and the reasoning behind it, so it is not re-litigated when the queryability tradeoff surfaces later.

## Decision

**Workflow nodes and edges are stored as JSON arrays on the `WorkflowDefinition` row, not as separate relational tables.**

```csharp
public class WorkflowDefinition : EntityBase
{
    public Guid? ProjectId { get; set; }
    public string Name { get; set; } = null!;
    public int Version { get; set; } = 1;

    [Column(TypeName = "jsonb")]
    public string Nodes { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string Edges { get; set; } = "[]";

    [Column(TypeName = "jsonb")]
    public string? CanvasState { get; set; }
}
```

The `WorkflowInterpreter` (Phase 8) deserializes these JSON arrays into `List<WorkflowNodeDef>` and `List<WorkflowEdgeDef>` value objects at orchestration start and walks them in-memory.

## Rationale

A workflow is an **atomic versioned artifact**, not a collection of independently-edited rows. The whole industry has converged on document-style storage for DAGs:

| System | Storage |
|--------|---------|
| Temporal | Compiled workflow code (DLL/JAR) |
| Airflow | Python files |
| Argo Workflows | YAML manifests |
| GitHub Actions | YAML |
| AWS Step Functions | JSON state machine |
| n8n | JSON document |
| Prefect | Python decorators |

None store nodes/edges as normalized relational rows. The reason is that workflows have a **single edit boundary** (the canvas) and a **single version boundary** (the whole definition). Normalizing splits both, creating coordination overhead that buys nothing.

## What we gain

1. **Atomic versioning** — every save is a coherent state; no half-applied edits across multiple tables
2. **Single-row optimistic concurrency** — `xmin`/`Version` on `WorkflowDefinition` is enough; no cross-table locking
3. **Editor round-trip is one query** — `SELECT nodes, edges FROM workflow_definitions WHERE id = ?`; no N+1, no joins
4. **Save is one UPDATE** — no diff-and-apply across inserts/updates/deletes on child tables
5. **Schema migrations don't break in-flight workflows** — node config shape can evolve per node type without DB migration
6. **Matches the canvas library output** — React Flow / Reactflow / Mermaid all emit and consume JSON DAGs directly
7. **Concurrent editing safety** — `Version` row-version detects mid-air collisions; with relational rows, two users editing different nodes silently merge in ways the editor never saw

## What we give up

1. **No SQL queries by node type** — `SELECT * FROM workflow_nodes WHERE type='agent_task'` does not exist
2. **No foreign keys from nodes to other entities** — e.g. a node referencing an agent name is just a string, not a FK to `AgentCatalog`
3. **No row-level audit on individual nodes** — audit is per-workflow-version, not per-node-edit

## Mitigations for the losses

### 1. Queryability via JSONB

PostgreSQL JSONB operators make the supposedly-lost queries trivial:

```sql
-- "Which workflows use the security.webapp.scanner agent?"
SELECT id, name, version
FROM workflow_definitions
WHERE EXISTS (
  SELECT 1
  FROM jsonb_array_elements(nodes::jsonb) AS n
  WHERE n->>'type' = 'agent_task'
    AND n->'config'->>'agentName' = 'security.webapp.scanner'
);

-- "How many decision nodes are in each workflow?"
SELECT id, name,
  (SELECT COUNT(*) FROM jsonb_array_elements(nodes::jsonb) AS n
   WHERE n->>'type' IN ('human_decision', 'ai_decision')) AS decision_count
FROM workflow_definitions;
```

If a query becomes hot, add a GIN index:

```sql
CREATE INDEX idx_workflow_nodes_jsonb
  ON workflow_definitions USING gin (nodes jsonb_path_ops);
```

### 2. Denormalized read-model (only if needed)

If application-level analytics need fast cross-workflow rollups, add a synchronous denormalized table populated on `WorkflowDefinition` save:

```sql
CREATE TABLE workflow_agent_usage (
  workflow_definition_id UUID NOT NULL REFERENCES workflow_definitions(id) ON DELETE CASCADE,
  workflow_version INT NOT NULL,
  agent_name TEXT NOT NULL,
  node_count INT NOT NULL,
  PRIMARY KEY (workflow_definition_id, workflow_version, agent_name)
);
```

This is **additive**, not architectural — defer until there is a real consumer.

### 3. Soft FK validation at save time

Node config strings that "should" reference other entities (e.g. `agentName` → `AgentCatalog.AgentName`) are validated in the `IWorkflowValidator` (Phase 4). Validation fails the save; the database does not need a FK to enforce this.

```csharp
// WorkflowValidator.cs
foreach (var node in nodes.Where(n => n.Type == "agent_task"))
{
    var agentName = node.Config?["agentName"];
    if (!await _catalog.ExistsAsync(agentName))
        errors.Add($"Node {node.Id} references unknown agent '{agentName}'");
}
```

## Consequences

### Positive
- Phase 1 entity model is simpler (no `WorkflowNode` / `WorkflowEdge` entities)
- Phase 4 endpoints are simpler (workflow CRUD is single-row operations)
- Phase 8 `WorkflowInterpreter` works on plain in-memory graph value objects
- React Flow canvas state round-trips without translation
- Workflow versioning is genuinely atomic

### Negative
- One-off SQL analytics queries require JSONB syntax (mitigated above)
- Engineers unfamiliar with JSONB may default to inefficient unpacking patterns; team conventions doc should include the patterns above
- No DB-level enforcement that node references point at real entities — validation lives in application code

### Risks
- If a future requirement demands graph-database queries (e.g. "find all workflows reachable from this workflow via sub-workflow nodes"), we may need a Phase-level investment in a property graph or denormalized closure table. Acceptable risk — not foreseeable from current requirements.

## Alternatives considered

### Alternative A: Normalized relational (WorkflowNode + WorkflowEdge tables)

- Pros: SQL queryability, FK integrity, row-level audit
- Cons: Atomic versioning is hard (transaction across N tables, optimistic concurrency across rows that may not exist yet), schema migrations break in-flight workflows, editor save logic is a diff-and-apply across 3 tables, no industry precedent at this scale
- **Rejected** — buys queries we mostly don't need at the cost of every operation we do need

### Alternative B: Hybrid — JSON for editing, materialized rows for execution

- Pros: best of both
- Cons: two sources of truth, sync logic, doubled storage, doubled migration cost
- **Rejected** — solving a hypothetical problem with real complexity

### Alternative C: Graph database (Neo4j, AGE on Postgres)

- Pros: native graph queries (path, reachability, centrality)
- Cons: massive infrastructure cost for a problem we don't have; ADR-015 (knowledge graph) addresses a different need (Apache AGE for knowledge), not workflow storage
- **Rejected** — premature for workflow definitions

## Compliance / Audit

The audit trail (ADR-008) captures `WorkflowDefinition` create/update events at the row level via the `xmin` row version. For per-node edit history, the `CanvasState` blob and `Version` increment record the user-visible change history. If finer-grained "who changed which node" attribution becomes a regulatory requirement, a `WorkflowDefinitionHistory` snapshot table can be added — current history is recovered by diffing successive `Version` snapshots.

## Acceptance Criteria

- [ ] `WorkflowDefinition.Nodes` and `WorkflowDefinition.Edges` are `jsonb` columns
- [ ] No `WorkflowNode` or `WorkflowEdge` entities in the Domain project
- [ ] `WorkflowInterpreter` deserializes JSON arrays into value objects (`WorkflowNodeDef`, `WorkflowEdgeDef`)
- [ ] `IWorkflowValidator` validates node references (e.g. `agentName`) against real entities at save time
- [ ] Team conventions doc documents JSONB query patterns for common ad-hoc queries

---

## References

- ADR-013 (workflow design — the canvas/editor/interpreter pattern)
- ADR-001 (workflow engine — Durable Task)
- ADR-004 (data layer — Postgres + JSONB conventions)
- `docs/008-plans/001-phase-1-domain-alignment.md` (entity rewrites)
- `docs/008-plans/008-phase-8-workflow-engine.md` (interpreter implementation)
