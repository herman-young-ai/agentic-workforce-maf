# ADR-015: Knowledge Graph Layer (Apache AGE)

**Status:** Accepted
**Date:** 2026-05-12
**Decision Makers:** Architecture team
**Research:** [R18-response-graphrag-knowledge-graph.md](../098-research/R18-response-graphrag-knowledge-graph.md)
**Depends on:** [ADR-004](ADR-004-data-layer.md), [ADR-010](ADR-010-context-assembly.md), [ADR-014](ADR-014-knowledge-memory.md)

---

## Context

The platform's knowledge layer (ADR-014) uses PostgreSQL + pgvector for semantic retrieval — finding similar learnings, deduplicating knowledge, ranking document chunks. This works well for "find things that sound like X."

Agents increasingly need to answer **relational** questions that vector search cannot: dependency chains, blast-radius analysis, compliance traceability (policy → control → finding), and cross-project impact analysis. These require traversing typed relationships between entities, not cosine similarity.

R18 research confirmed that **Apache AGE is GA on Azure Database for PostgreSQL Flexible Server** (PG 16/17/18), preserving the single-database principle (ADR-004).

## Decision

**Apache AGE for graph traversal alongside pgvector for vector similarity, in the same PostgreSQL Flexible Server instance. Graph entities link to relational entities via UUID foreign keys. Entity extraction extends the existing `KnowledgeExtractor` (ADR-014).**

---

## Graph Schema

### Design Principles

1. **Graph supplements relational — does not replace it.** First-class domain entities (Project, Task, Agent, etc.) remain EF Core entities. The graph stores **relationships between entities** and **lightweight entity references** (not full copies).
2. **Nodes reference relational rows via UUID.** Every graph node carries `entity_id` (FK to the relational table) and `entity_table` (which table). The graph is a relationship index, not a second source of truth.
3. **Edges are first-class.** Edges carry properties: `confidence`, `discovered_by`, `discovered_at`, `evidence_task_id`, `status`. Edges can be retracted (status = `retracted`), following the same principle as learnings (ADR-014).
4. **Single graph per database.** One AGE graph named `knowledge_graph` holds all vertex/edge labels. Project scoping is a property on nodes, not separate graphs.

### Vertex Labels

| Label | Properties | Relational FK | Purpose |
|-------|-----------|---------------|---------|
| `Component` | `name`, `type` (api/service/library/database/queue), `project_id`, `entity_id`, `entity_table` | `ProjectArtifact` or PCD `architecture.components` | Software component in a project |
| `Policy` | `name`, `framework` (OWASP/PCI/SOX/POPIA/FCA), `version`, `external_ref` | None (external reference) | Regulatory policy or standard |
| `Control` | `name`, `policy_id`, `description`, `category` | None (external reference) | Specific control within a policy |
| `Finding` | `title`, `severity` (critical/high/medium/low/info), `status` (open/remediated/accepted/retracted), `project_id`, `entity_id`, `entity_table` | `ProjectLearning` (kind=domain_insight) or `ProjectArtifact` | Security/compliance finding |
| `Agent` | `name`, `category`, `entity_id` | `AgentCatalog` | Agent in the catalog |
| `Library` | `name`, `version`, `ecosystem` (nuget/npm/pip), `project_id` | Extracted from dependency files | Software dependency |
| `CVE` | `cve_id`, `severity`, `cvss_score`, `published_at` | None (external reference) | Known vulnerability |
| `OWASPCategory` | `code` (A01-A10), `name`, `year` | None (external reference) | OWASP Top 10 category |
| `Team` | `name`, `project_id`, `entity_id` | `ProjectMember` (grouped) | Team or individual assignment |

### Edge Labels

| Label | From → To | Properties | Purpose |
|-------|-----------|-----------|---------|
| `DEPENDS_ON` | Component → Component | `type` (calls/imports/reads/writes), `confidence` | Runtime or compile-time dependency |
| `USES_LIBRARY` | Component → Library | `version_constraint`, `scope` (runtime/dev/test) | Package dependency |
| `SCANNED_BY` | Component → Agent | `last_scan_at`, `task_id` | Which agent last analysed this component |
| `HAS_FINDING` | Component → Finding | `task_id`, `discovered_at` | Finding against a component |
| `MAPS_TO` | Finding → OWASPCategory | `confidence` | OWASP classification |
| `REQUIRES` | Policy → Control | `section_ref` | Policy requires this control |
| `EVIDENCED_BY` | Control → Finding | `evidence_type` (positive/negative/gap) | Finding provides evidence for a control |
| `PRODUCED_BY` | Finding → AgenticTask (via `entity_id`) | `task_id` | Which task produced this finding |
| `HAS_VULNERABILITY` | Library → CVE | `affected_versions`, `fixed_in` | Known vulnerability in library |
| `REMEDIATES` | Finding → Finding | `remediation_type` (fix/mitigate/accept) | Remediation relationship |
| `ASSIGNED_TO` | Component → Team | `role` (owner/contributor) | Ownership |

### SQL DDL (AGE Setup)

```sql
-- Enable extension (one-time, requires azure.extensions allowlist)
CREATE EXTENSION IF NOT EXISTS age;
LOAD 'age';
SET search_path = ag_catalog, "$user", public;

-- Create the graph
SELECT create_graph('knowledge_graph');

-- Vertex labels
SELECT create_vlabel('knowledge_graph', 'Component');
SELECT create_vlabel('knowledge_graph', 'Policy');
SELECT create_vlabel('knowledge_graph', 'Control');
SELECT create_vlabel('knowledge_graph', 'Finding');
SELECT create_vlabel('knowledge_graph', 'Agent');
SELECT create_vlabel('knowledge_graph', 'Library');
SELECT create_vlabel('knowledge_graph', 'CVE');
SELECT create_vlabel('knowledge_graph', 'OWASPCategory');
SELECT create_vlabel('knowledge_graph', 'Team');

-- Edge labels
SELECT create_elabel('knowledge_graph', 'DEPENDS_ON');
SELECT create_elabel('knowledge_graph', 'USES_LIBRARY');
SELECT create_elabel('knowledge_graph', 'SCANNED_BY');
SELECT create_elabel('knowledge_graph', 'HAS_FINDING');
SELECT create_elabel('knowledge_graph', 'MAPS_TO');
SELECT create_elabel('knowledge_graph', 'REQUIRES');
SELECT create_elabel('knowledge_graph', 'EVIDENCED_BY');
SELECT create_elabel('knowledge_graph', 'PRODUCED_BY');
SELECT create_elabel('knowledge_graph', 'HAS_VULNERABILITY');
SELECT create_elabel('knowledge_graph', 'REMEDIATES');
SELECT create_elabel('knowledge_graph', 'ASSIGNED_TO');

-- Performance indexes (per R18 research: BTREE on id/start_id/end_id, GIN on properties)
-- AGE auto-creates BTREE on id for each label
-- Add GIN on properties for each heavily-queried label:
CREATE INDEX idx_component_props ON knowledge_graph."Component" USING GIN (properties);
CREATE INDEX idx_finding_props ON knowledge_graph."Finding" USING GIN (properties);
CREATE INDEX idx_library_props ON knowledge_graph."Library" USING GIN (properties);

-- Expression indexes on hot property keys:
CREATE INDEX idx_component_project ON knowledge_graph."Component"
    USING BTREE ((properties->>'project_id'));
CREATE INDEX idx_finding_severity ON knowledge_graph."Finding"
    USING BTREE ((properties->>'severity'));
CREATE INDEX idx_finding_project ON knowledge_graph."Finding"
    USING BTREE ((properties->>'project_id'));
```

---

## C# Integration Layer

### Repository Pattern (Not EF Core)

Per R18 research, EF Core is a poor fit for graph queries. The graph layer uses raw `NpgsqlCommand` via `Npgsql.Age`, sharing the same `NpgsqlDataSource` (and transactions when needed) as EF Core.

```csharp
public interface IKnowledgeGraphRepository
{
    // Node operations
    Task<GraphNode?> TryGetNodeAsync(string label, string entityId, CancellationToken ct);
    Task<GraphNode> UpsertNodeAsync(string label, string entityId,
        Dictionary<string, object> properties, CancellationToken ct);
    Task RetractNodeAsync(string label, string entityId, CancellationToken ct);
    Task PromoteNodeAsync(string label, string entityId, CancellationToken ct);

    // Edge operations
    Task<GraphEdge> UpsertEdgeAsync(string edgeLabel,
        string fromLabel, string fromEntityId,
        string toLabel, string toEntityId,
        Dictionary<string, object> properties, CancellationToken ct);

    // Traversal queries
    Task<IReadOnlyList<GraphPath>> TraverseAsync(
        string startLabel, string startEntityId,
        string? edgeFilter, int maxDepth, CancellationToken ct);
    Task<IReadOnlyList<GraphNode>> BlastRadiusAsync(
        string startLabel, string startEntityId,
        int maxDepth, CancellationToken ct);
    Task<IReadOnlyList<GraphPath>> ComplianceChainAsync(
        string policyName, string? controlName, CancellationToken ct);
    Task<IReadOnlyList<GraphNode>> ImpactAnalysisAsync(
        string libraryName, string? version, CancellationToken ct);

    // Hybrid: graph-scoped vector search
    Task<IReadOnlyList<Guid>> GraphScopedEntityIdsAsync(
        string startLabel, string startEntityId,
        string targetLabel, int maxDepth, CancellationToken ct);
}
```

### Cypher Query Implementation

```csharp
internal sealed class AgeKnowledgeGraphRepository : IKnowledgeGraphRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public AgeKnowledgeGraphRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<GraphPath>> TraverseAsync(
        string startLabel, string startEntityId,
        string? edgeFilter, int maxDepth, CancellationToken ct)
    {
        var edgePattern = edgeFilter is not null ? $":{edgeFilter}" : "";
        // Only traverse active nodes — pending/retracted are excluded
        var cypher = $"""
            SELECT * FROM cypher('knowledge_graph', $$
                MATCH p = (s:{startLabel} {{entity_id: '{startEntityId}', status: 'active'}})
                          -[{edgePattern}*1..{maxDepth}]->(t)
                WHERE t.status = 'active'
                RETURN p
            $$) AS (path agtype);
            """;

        await using var cmd = _dataSource.CreateCommand(cypher);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var paths = new List<GraphPath>();
        while (await reader.ReadAsync(ct))
        {
            paths.Add(GraphPath.FromAgtype(reader.GetFieldValue<Agtype>(0)));
        }
        return paths;
    }

    public async Task<IReadOnlyList<GraphNode>> BlastRadiusAsync(
        string startLabel, string startEntityId,
        int maxDepth, CancellationToken ct)
    {
        // Follow all outgoing edges to find everything affected (active only)
        var cypher = $"""
            SELECT * FROM cypher('knowledge_graph', $$
                MATCH (s:{startLabel} {{entity_id: '{startEntityId}', status: 'active'}})
                      -[*1..{maxDepth}]->(affected)
                WHERE affected.status = 'active'
                RETURN DISTINCT affected
            $$) AS (node agtype);
            """;

        await using var cmd = _dataSource.CreateCommand(cypher);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var nodes = new List<GraphNode>();
        while (await reader.ReadAsync(ct))
        {
            nodes.Add(GraphNode.FromAgtype(reader.GetFieldValue<Agtype>(0)));
        }
        return nodes;
    }

    public async Task<IReadOnlyList<GraphPath>> ComplianceChainAsync(
        string policyName, string? controlName, CancellationToken ct)
    {
        // Policy → Control → Finding chain
        var controlFilter = controlName is not null
            ? $"{{name: '{controlName}'}}" : "";
        var cypher = $"""
            SELECT * FROM cypher('knowledge_graph', $$
                MATCH p = (pol:Policy {{name: '{policyName}'}})
                          -[:REQUIRES]->(ctrl:Control {controlFilter})
                          -[:EVIDENCED_BY]->(f:Finding)
                RETURN p
            $$) AS (path agtype);
            """;

        await using var cmd = _dataSource.CreateCommand(cypher);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var paths = new List<GraphPath>();
        while (await reader.ReadAsync(ct))
        {
            paths.Add(GraphPath.FromAgtype(reader.GetFieldValue<Agtype>(0)));
        }
        return paths;
    }

    public async Task<GraphNode> UpsertNodeAsync(string label, string entityId,
        Dictionary<string, object> properties, CancellationToken ct)
    {
        properties["entity_id"] = entityId;
        properties["updated_at"] = DateTimeOffset.UtcNow.ToString("O");
        var propsJson = System.Text.Json.JsonSerializer.Serialize(properties);

        // Two-step upsert: MERGE creates if absent, then SET properties.
        // Status is handled in application code: if the node already exists
        // as "active", ApplyExtractionsAsync preserves its status by removing
        // the "status" key from the update properties dict (see below).
        var cypher = $"""
            SELECT * FROM cypher('knowledge_graph', $$
                MERGE (n:{label} {{entity_id: '{entityId}'}})
                SET n += {propsJson}
                RETURN n
            $$) AS (node agtype);
            """;

        await using var cmd = _dataSource.CreateCommand(cypher);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return GraphNode.FromAgtype(reader.GetFieldValue<Agtype>(0));
    }
}
```

### Agent Tools (Native AIFunction)

Agents access the graph via registered tools, not raw Cypher. These are **native `AIFunction` tools** (in-process, per ADR-011 pattern) — not MCP servers — because they share the same `NpgsqlDataSource` and require no process isolation.

```csharp
/// <summary>
/// Graph tools registered with agents via AIFunctionFactory.Create().
/// In-process, zero-overhead, shares NpgsqlDataSource with EF Core.
/// </summary>
public class KnowledgeGraphTools
{
    private readonly IKnowledgeGraphRepository _graph;

    public KnowledgeGraphTools(IKnowledgeGraphRepository graph) => _graph = graph;

    [Description(
        "Traverse the knowledge graph from a starting entity. " +
        "Returns connected entities up to maxDepth hops away. " +
        "Use for dependency chains, impact analysis, and traceability.")]
    public async Task<GraphTraversalResult> TraverseGraph(
        [Description("Entity type: Component, Policy, Control, Finding, Library, Agent, CVE, OWASPCategory, Team")]
        string startLabel,
        [Description("The entity_id (UUID) of the starting node")]
        string startEntityId,
        [Description("Optional: filter to specific edge type (e.g. DEPENDS_ON, HAS_FINDING)")]
        string? edgeFilter = null,
        [Description("Maximum traversal depth (1-5, default 3)")]
        int maxDepth = 3)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 5);
        var paths = await _graph.TraverseAsync(startLabel, startEntityId, edgeFilter, maxDepth, default);
        return new GraphTraversalResult(paths);
    }

    [Description(
        "Analyze blast radius: what is affected if this entity fails or is compromised? " +
        "Follows all outgoing dependency edges.")]
    public async Task<BlastRadiusResult> AnalyzeBlastRadius(
        [Description("Entity type: Component, Library")]
        string entityType,
        [Description("The entity_id (UUID) of the entity to analyze")]
        string entityId,
        [Description("Maximum depth to traverse (1-5, default 3)")]
        int maxDepth = 3)
    {
        maxDepth = Math.Clamp(maxDepth, 1, 5);
        var affected = await _graph.BlastRadiusAsync(entityType, entityId, maxDepth, default);
        return new BlastRadiusResult(entityId, affected);
    }

    [Description(
        "Trace compliance: find the full evidence chain from a policy/control to findings. " +
        "Returns Policy → Control → Finding paths.")]
    public async Task<ComplianceChainResult> TraceCompliance(
        [Description("Policy name (e.g. 'OWASP Top 10 2025', 'PCI DSS 4.0')")]
        string policyName,
        [Description("Optional: specific control name to filter")]
        string? controlName = null)
    {
        var paths = await _graph.ComplianceChainAsync(policyName, controlName, default);
        return new ComplianceChainResult(policyName, controlName, paths);
    }

    [Description(
        "Check what projects and components are affected by a library vulnerability.")]
    public async Task<ImpactAnalysisResult> AnalyzeLibraryImpact(
        [Description("Library name (e.g. 'Newtonsoft.Json', 'lodash')")]
        string libraryName,
        [Description("Optional: specific version to check")]
        string? version = null)
    {
        var affected = await _graph.ImpactAnalysisAsync(libraryName, version, default);
        return new ImpactAnalysisResult(libraryName, version, affected);
    }
}

// Registration with agent (in AgentFactory):
// var graphTools = AIFunctionFactory.Create(
//     new KnowledgeGraphTools(_graphRepo),
//     [nameof(KnowledgeGraphTools.TraverseGraph),
//      nameof(KnowledgeGraphTools.AnalyzeBlastRadius),
//      nameof(KnowledgeGraphTools.TraceCompliance),
//      nameof(KnowledgeGraphTools.AnalyzeLibraryImpact)]);
```

---

## Entity Extraction Pipeline

### Extension of KnowledgeExtractor (ADR-014)

The existing `KnowledgeExtractor` extracts learnings and PCD updates after each task. We extend it with a third extraction step: **graph entity and relationship extraction**.

```csharp
public class KnowledgeExtractor
{
    public async Task ExtractAsync(TaskExecution execution, Guid projectId)
    {
        // 1. Extract learnings (existing — ADR-014)
        var candidates = await _extractionAgent.RunAsync<LearningCandidates>(...);
        foreach (var candidate in candidates.Items)
            await DeduplicateAndStore(candidate, execution, projectId);

        // 2. Extract PCD updates (existing — ADR-014)
        var pcdUpdates = await _extractionAgent.RunAsync<PcdUpdates>(...);
        if (pcdUpdates.HasChanges)
            await _pcdService.ApplyUpdatesAsync(projectId, pcdUpdates);

        // 3. NEW: Extract graph entities and relationships
        if (ShouldExtractGraph(execution))
        {
            var graphUpdates = await _graphExtractionAgent.RunAsync<GraphExtractionResult>(
                $"""
                Analyze this agent execution output and extract entities and relationships
                for the knowledge graph.

                Extract:
                - Components mentioned (APIs, services, databases, queues, libraries)
                - Dependencies between components (calls, imports, reads, writes)
                - Findings (security issues, quality issues, compliance gaps)
                - Which component each finding applies to
                - OWASP category mappings for security findings
                - Library dependencies and known vulnerabilities (CVEs)

                Return structured JSON. Only extract what is explicitly stated or
                strongly implied — do not speculate.

                Agent: {execution.AgentName}
                Task objective: {execution.TaskObjective}
                Output: {execution.OutputData}
                """);

            await _graphService.ApplyExtractionsAsync(
                projectId, execution.TaskId, graphUpdates);
        }
    }

    private static bool ShouldExtractGraph(TaskExecution execution)
    {
        // Only extract graph data from relevant task types
        return execution.DomainTags.Overlaps(
            ["security", "compliance", "architecture", "dependencies", "scanning"]);
    }
}
```

### Graph Extraction Result Schema

```csharp
public record GraphExtractionResult
{
    public GraphEntityCandidate[] Entities { get; init; } = [];
    public GraphRelationshipCandidate[] Relationships { get; init; } = [];
}

public record GraphEntityCandidate
{
    public required string Label { get; init; }      // Component, Finding, Library, CVE
    public required string Name { get; init; }
    public required string? Type { get; init; }       // api, service, library, etc.
    public string? Severity { get; init; }             // for Findings
    public Dictionary<string, object> Properties { get; init; } = new();
}

public record GraphRelationshipCandidate
{
    public required string EdgeLabel { get; init; }    // DEPENDS_ON, HAS_FINDING, etc.
    public required string FromLabel { get; init; }
    public required string FromName { get; init; }
    public required string ToLabel { get; init; }
    public required string ToName { get; init; }
    public decimal Confidence { get; init; } = 0.8m;
    public Dictionary<string, object> Properties { get; init; } = new();
}
```

### Graph Service (Upsert with Deduplication)

```csharp
internal sealed class GraphService
{
    private readonly IKnowledgeGraphRepository _graph;

    public async Task ApplyExtractionsAsync(
        Guid projectId, Guid taskId, GraphExtractionResult result)
    {
        foreach (var entity in result.Entities)
        {
            var entityId = DeterministicEntityId(entity.Label, entity.Name, projectId);

            // Check if this entity already exists (and is active)
            var existing = await _graph.TryGetNodeAsync(entity.Label, entityId, default);

            var props = new Dictionary<string, object>(entity.Properties)
            {
                ["name"] = entity.Name,
                ["project_id"] = projectId.ToString(),
                ["discovered_by_task"] = taskId.ToString(),
            };
            if (entity.Type is not null) props["type"] = entity.Type;
            if (entity.Severity is not null) props["severity"] = entity.Severity;

            if (existing is null)
            {
                // New entity: starts as "pending" — requires human promotion to "active"
                // before it appears in context assembly (Layer 4a) or agent tool results.
                // Matches the learning promotion security gate (ADR-014 §C16).
                props["status"] = "pending";
            }
            // else: existing entity — preserve current status (active stays active,
            // retracted stays retracted). MERGE updates properties only.

            await _graph.UpsertNodeAsync(entity.Label, entityId, props, default);
        }

        foreach (var rel in result.Relationships)
        {
            var fromId = DeterministicEntityId(rel.FromLabel, rel.FromName, projectId);
            var toId = DeterministicEntityId(rel.ToLabel, rel.ToName, projectId);

            var props = new Dictionary<string, object>(rel.Properties)
            {
                ["confidence"] = rel.Confidence,
                ["discovered_by_task"] = taskId.ToString(),
                ["discovered_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            await _graph.UpsertEdgeAsync(
                rel.EdgeLabel, rel.FromLabel, fromId, rel.ToLabel, toId, props, default);
        }
    }

    /// <summary>
    /// Deterministic entity_id from label + name + optional project scope.
    /// Global entities (Policy, CVE, OWASPCategory) ignore projectId.
    /// </summary>
    private static string DeterministicEntityId(string label, string name, Guid projectId)
    {
        var scope = label is "Policy" or "Control" or "CVE" or "OWASPCategory"
            ? "global"
            : projectId.ToString();
        return GuidV5($"{label}:{scope}:{name.ToLowerInvariant()}").ToString();
    }

    private static Guid GuidV5(string input)
    {
        // UUIDv5 from SHA-256 of the input (deterministic, idempotent)
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant 1
        return new Guid(hash.AsSpan(0, 16));
    }
}
```

---

## Context Assembly Integration (ADR-010 Update)

### New Priority Layer: Graph Context

Graph context slots in at **priority 4a** — after decisions but before existing findings:

| Priority | Layer | Source | Max Tokens | Trimmed? |
|----------|-------|--------|-----------|----------|
| 0 | PCD (identity, principles, guardrails) | DB | — | Never |
| 0a | Task definition | Execution plan | — | Never |
| 1 | Upstream task inputs | Previous task outputs | Variable | Summarised if over budget |
| 2 | Platform learnings | DB (platform) | ~500 | Skipped if budget tight |
| 3 | Project learnings | DB (project) | ~1000 | Skipped if budget tight |
| 4 | Active decisions | DB | ~500 | Skipped if budget tight |
| **4a** | **Graph context (dependency/compliance chains)** | **AGE traversal** | **~800** | **Skipped if budget tight** |
| 5 | Existing findings (dedup context) | DB | ~1000 | Trimmed |
| 6 | Uploaded document chunks | pgvector search | ~2000 | Trimmed |
| 7 | Code Map | Generated | Only coding tasks | Token-capped |
| 8 (lowest) | Execution history | DB | Variable | Trimmed first |

### When Graph Context Is Injected

Graph context is **conditional** — only assembled when the task's domain tags include relevant domains:

```csharp
// In ProjectContextProvider.InvokingAsync()

if (domainTags.Overlaps(["security", "compliance", "architecture", "dependencies"]))
{
    var graphContext = await _graphContextBuilder.BuildAsync(projectId, taskDefinition, ct);
    if (graphContext is not null && tokenBudget.HasRoomFor(graphContext.EstimatedTokens))
    {
        messages.Add(SystemMsg(
            $"<knowledge-graph>\n" +
            $"The following shows relevant entity relationships from the knowledge graph.\n" +
            $"{graphContext.FormattedText}\n" +
            $"</knowledge-graph>"));
    }
}
```

### Graph Context Builder

```csharp
internal sealed class GraphContextBuilder
{
    private readonly IKnowledgeGraphRepository _graph;

    public async Task<GraphContextPacket?> BuildAsync(
        Guid projectId, TaskDefinition task, CancellationToken ct)
    {
        var sections = new List<string>();

        // 1. Component dependency map for the components mentioned in the task
        var components = ExtractComponentNames(task);
        foreach (var component in components.Take(3)) // limit to avoid context bloat
        {
            var deps = await _graph.TraverseAsync(
                "Component", DeterministicEntityId("Component", component, projectId),
                "DEPENDS_ON", maxDepth: 2, ct);
            if (deps.Any())
                sections.Add($"## {component} dependencies\n{FormatPaths(deps)}");
        }

        // 2. Open findings on relevant components
        var findings = await _graph.TraverseAsync(
            "Component", projectId.ToString(),
            "HAS_FINDING", maxDepth: 1, ct);
        if (findings.Any())
            sections.Add($"## Open findings\n{FormatFindings(findings)}");

        if (!sections.Any()) return null;

        var text = string.Join("\n\n", sections);
        return new GraphContextPacket(text, EstimateTokens(text));
    }
}
```

---

## Hybrid Queries (Graph + Vector)

Two patterns for combining graph traversal with vector similarity:

### Pattern 1: Graph → Vector (scope narrowing)

Use the graph to identify a relevant subgraph, then vector-search within that scope:

```csharp
// "Find learnings similar to X that relate to Component Y within 2 hops"
public async Task<IReadOnlyList<ProjectLearning>> GraphScopedSimilaritySearch(
    Guid projectId, string componentName, Vector queryEmbedding, int maxResults)
{
    // Step 1: Graph traversal to get entity IDs within 2 hops
    var relatedEntityIds = await _graph.GraphScopedEntityIdsAsync(
        "Component", DeterministicEntityId("Component", componentName, projectId),
        "Finding", maxDepth: 2, default);

    // Step 2: Vector search scoped to those entities
    return await _learningRepo.FindSimilarScopedAsync(
        projectId, queryEmbedding, relatedEntityIds, maxResults);
}
```

```sql
-- The pgvector query with graph-derived scope
SELECT l.* FROM project_learnings l
WHERE l.project_id = @projectId
  AND l.status = 'active'
  AND l.id = ANY(@relatedEntityIds)
ORDER BY l.embedding <=> @queryVector
LIMIT @maxResults;
```

### Pattern 2: Vector → Graph (entity grounding)

Find similar items via vector search, then expand their graph neighbourhood:

```csharp
// "Find similar findings and show what they're connected to"
public async Task<EnrichedSearchResult> VectorThenGraphSearch(
    Guid projectId, Vector queryEmbedding, int topK)
{
    // Step 1: Vector search for top-K similar findings
    var similar = await _learningRepo.FindSimilarAsync(projectId, queryEmbedding, topK);

    // Step 2: For each result, expand graph neighbourhood
    var enriched = new List<EnrichedFinding>();
    foreach (var finding in similar.Take(5))
    {
        var neighbourhood = await _graph.TraverseAsync(
            "Finding", finding.Id.ToString(), edgeFilter: null, maxDepth: 1, default);
        enriched.Add(new(finding, neighbourhood));
    }

    return new EnrichedSearchResult(enriched);
}
```

---

## Bulk Graph Population (Microsoft GraphRAG)

For initial population and periodic re-extraction from unstructured documents (policies, standards), use Microsoft GraphRAG as an **offline ETL pipeline**:

```
Unstructured docs (policies, standards, playbooks)
    │
    ▼
Microsoft GraphRAG (Python, containerised)
    │ Entity extraction + community detection
    │ LLM: Foundry Claude via LiteLLM
    │ Embeddings: text-embedding-3-small (1536-d)
    ▼
Parquet artefacts (entities, relationships, communities)
    │
    ▼
Bulk loader (Python or C# script)
    │ Translates to AGE MERGE statements
    │ Embeds into pgvector alongside existing chunks
    ▼
PG + AGE + pgvector (runtime store)
```

This runs as a scheduled job, not a runtime dependency. GraphRAG is **not embedded** in the .NET service — it's a batch pipeline whose outputs are loaded into the supported store.

---

## Operational Considerations

### AGE-Specific Caveats

| Concern | Mitigation |
|---------|-----------|
| AGE blocks in-place PG major-version upgrades | Plan upgrades via logical replication or dump/restore (same as TimescaleDB) |
| Community NuGet packages (`Npgsql.Age`) | Vet supply chain; consider vendoring the source for regulated compliance |
| No published benchmarks at our exact scale | **1-2 sprint PoC required** — run real queries on representative data with `EXPLAIN (ANALYZE, BUFFERS)` before committing |
| AGE on PG 17 had stability issues (March 2026) | Confirm with `SHOW azure.extensions;` in SA North; fall back to PG 16 if needed |
| Graph properties (JSONB-like `agtype`) can balloon | Cap property size; store large payloads in relational tables, graph nodes carry references only |

### Monitoring

- **Graph size**: track vertex/edge count per label (scheduled query)
- **Query latency**: instrument `AgeKnowledgeGraphRepository` methods with OTel spans
- **Index effectiveness**: weekly `EXPLAIN (ANALYZE, BUFFERS)` on hot Cypher queries
- **Extraction quality**: track graph entity extraction rate per task type (how many entities/relationships extracted per execution)

### Backup and DR

No additional complexity — AGE data lives in PostgreSQL tables (`ag_catalog` schema). Existing Flex Server backup, PITR, and cross-region read replica cover the graph data automatically.

---

## Migration Path

### Phase 1: Foundation (Sprint N)
- Enable AGE extension on PG Flex Server
- Create `knowledge_graph` with vertex/edge labels and indexes
- Implement `IKnowledgeGraphRepository` + `AgeKnowledgeGraphRepository`
- Vet and vendor `Npgsql.Age` NuGet package
- **PoC**: run representative Cypher queries, validate p95 latency

### Phase 2: Agent Tools (Sprint N+1)
- Implement `KnowledgeGraphTools` (AIFunction)
- Register tools with relevant agents (security reviewer, compliance checker, planner)
- Agents can now query the graph on-demand via tools

### Phase 3: Extraction (Sprint N+2)
- Extend `KnowledgeExtractor` with graph extraction step
- Graph auto-populates from agent execution outputs
- Human review of extracted entities (same pattern as learning promotion)

### Phase 4: Context Assembly (Sprint N+3)
- Add priority 4a graph context layer to `ContextAssembler`
- Implement `GraphContextBuilder`
- Graph context automatically injected for relevant task types

### Phase 5: Bulk Population (Sprint N+4, optional)
- Containerise Microsoft GraphRAG pipeline
- Run against policy/standards corpus
- Bulk-load into AGE + pgvector
- Schedule periodic re-extraction

---

## Consequences

- **Single database preserved** — no new infrastructure, same backup/DR/audit story
- **Knowledge layer enriched** — agents can answer relational questions that vector search alone cannot
- **Extraction cost increases** — graph extraction adds a second Haiku call per relevant task (~$0.001)
- **Graph data requires human oversight** — extracted entities follow the same pending → active promotion pattern as learnings (ADR-014)
- **AGE is a dependency** — blocks in-place PG upgrades, adds extension management overhead
- **PoC is mandatory** — do not skip Phase 1 validation of query performance at scale
- **Graph is supplementary** — if AGE proves problematic, the platform functions fully on pgvector alone (graceful degradation)

### Principle Compliance

- **P14 Secure by Default:** Graph queries are scoped by `project_id` — cross-project graph traversal is denied by default. Graph extraction is disabled until explicitly enabled per project template.
- **P16 Single Source of Truth:** The graph layer is derived from authoritative relational data (tasks, learnings, artifacts). Graph entities are never the source of truth — if the graph is rebuilt from scratch, no data is lost.
- **P17 Human Authority:** Extracted graph entities follow the pending → active promotion pattern (same as learnings). Humans can retract graph relationships. The graph informs but does not dictate agent decisions.
- **P18 Idempotency:** Graph extraction is idempotent — re-extracting from the same task output produces the same entities/relationships. Duplicate extraction calls do not create duplicate nodes.
- **P19 Bounded Resource Usage:** Graph queries have explicit depth limits (max traversal depth), result limits (max nodes returned), and timeouts. Unbounded graph traversals do not exist.
- **P20 Version Everything:** Graph schema (node types, relationship types) is versioned. Schema evolution is backward-compatible — old relationships coexist with new ones.
- **P21 Explicit Over Implicit:** Graph node types and relationship types are declared in a schema manifest — not discovered from data. Each agent's graph query capabilities are explicitly configured.
