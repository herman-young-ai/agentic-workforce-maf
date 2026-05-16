# R18: GraphRAG — Knowledge Graph Layer for Context Assembly

## Prompt for claude.ai

---

I am building an AI agent orchestration platform ("Agentic Workforce Platform") in C# using Microsoft Agent Framework (MAF) on Azure. The platform targets regulated financial services (dual-regulated FCA/PRA + SARB/PA). We already have a working knowledge layer based on PostgreSQL + pgvector (1536-d embeddings, cosine similarity, hybrid structured + vector search). This serves us well for semantic retrieval.

We now want to evaluate adding a **knowledge graph layer alongside pgvector** to support relational queries that vector search cannot answer — dependency chains, impact analysis, compliance traceability, and multi-hop reasoning.

I need a **practical feasibility assessment and integration design** — not theory. What works today on Azure PostgreSQL Flexible Server, what's the implementation path, and is it worth the complexity for our use cases?

### What We Have Today (ADR-014)

**Five knowledge types, all in PostgreSQL + pgvector:**

| Type | What It Stores | How It's Queried |
|---|---|---|
| PCD (Project Context Document) | Versioned JSON: identity, architecture, principles, guardrails | Direct load, never trimmed from context |
| Learnings | Agent-discovered patterns with confidence, occurrence_count, domain_tags, 1536-d embedding | Cosine similarity + domain tag filter |
| Decisions | Explicit architectural choices with rationale, domain, status | Domain filter + status filter |
| Intent | Versioned understanding of project objective | Direct load by project |
| Platform Knowledge | Promoted learnings (cross-project, read-only) | Same as Learnings but platform-scoped |

**Context assembly pipeline (ADR-010):** `ContextAssembler` loads layers by priority, applies token budget, trims lower-priority layers first. PCD and task definition are never trimmed.

**Database:** Azure Database for PostgreSQL Flexible Server (PG 16/17), extensions: `vector`, `pg_diskann`, `pgaudit`, `pg_partman`, `uuid-ossp`. Single database principle (ADR-004).

### The Gap: Relational Questions Vector Search Cannot Answer

Our agents increasingly need to answer questions that require **traversing relationships**, not finding similar text:

| Question | Why Vector RAG Fails | What Graph Solves |
|---|---|---|
| "What is the blast radius if the Payments API auth is bypassed?" | No dependency data between components | Traverse: PaymentsAPI → calls → AccountService → reads → CustomerPII |
| "Trace this OWASP finding back to the policy that requires it" | Similarity finds OWASP text, not the policy→control→finding chain | Explicit: Policy → Control → Requirement → Finding → Remediation |
| "What other projects had similar issues with this component?" | Cross-project similarity works, but misses structural relationships | Traverse: Component → usedIn → Project → hadFinding → similar pattern |
| "If we patch this library, what agents/tools are affected?" | No dependency graph | Library → usedBy → Tool → registeredWith → Agent → assignedTo → Project |
| "Show the full compliance evidence chain for this control" | Finds related documents but can't reconstruct the chain | Control → evidencedBy → Finding → producedBy → Task → inProject → Project |

### Options to Evaluate

**Option A: Apache AGE extension on PostgreSQL**
- Graph queries (openCypher) inside existing PostgreSQL
- Same database, same connection, same transaction
- Single operational surface
- Question: Is AGE available on Azure PG Flexible Server? Maturity?

**Option B: Recursive CTEs + JSONB adjacency lists (pure PostgreSQL)**
- No new extensions — model graph as relational tables with FK edges
- Use `WITH RECURSIVE` CTEs for traversal
- JSONB properties on nodes and edges
- Question: How far can this go before it becomes unwieldy? Performance at depth 4-5?

**Option C: Dedicated graph database sidecar (Neo4j/Memgraph)**
- Purpose-built graph engine alongside PostgreSQL
- Cypher query language, optimised traversal
- Question: How does this affect our single-database principle? Sync complexity? Azure hosting options?

**Option D: pg_graphql or similar lightweight PostgreSQL graph extension**
- GraphQL-style queries over relational schema
- Question: Does this actually solve graph traversal, or is it just a query syntax layer?

**Option E: Microsoft GraphRAG library + PostgreSQL storage**
- Microsoft's open-source GraphRAG (entity extraction → community detection → graph construction)
- Question: Does this work with non-OpenAI models (Claude)? Can it use PostgreSQL as storage backend? Is it production-ready?

### Comparison Criteria

For each option, assess:

| Criterion | A (AGE) | B (CTEs) | C (Neo4j) | D (pg_graphql) | E (MS GraphRAG) |
|---|---|---|---|---|---|
| Azure PG Flexible Server compatibility | | | | | |
| Single-database principle preserved | | | | | |
| Query expressiveness (multi-hop, path finding, pattern matching) | | | | | |
| Performance at depth 4-5 with 10k-100k nodes | | | | | |
| EF Core integration story | | | | | |
| Operational complexity (backup, monitoring, failover) | | | | | |
| Works alongside pgvector (hybrid: graph + vector) | | | | | |
| Entity extraction from LLM outputs (auto-population) | | | | | |
| Maturity / production readiness | | | | | |
| C# / .NET SDK availability | | | | | |

### Specific Technical Questions

**Q1: Azure PG Flexible Server extension availability**
Which graph-related extensions are actually available on Azure Database for PostgreSQL Flexible Server? Check: `age`, `pg_graphql`, `pgRouting`, or any graph-capable extension. This is the critical blocker — if none are available, Options A/D are off the table.

**Q2: Relational graph modelling in pure PostgreSQL**
Show a schema design (5-10 tables) for a knowledge graph using standard relational tables:
- Node types: Component, Policy, Control, Finding, Agent, Tool, Project, Library
- Edge types: dependsOn, implements, evidencedBy, usedBy, producedBy, assignedTo
- Properties on both nodes and edges (JSONB)
- Show a recursive CTE that answers: "What is the full dependency chain from Component X, depth 5?"

**Q3: Entity extraction pipeline**
How do we automatically populate the graph from agent task outputs? Show the pattern:
- Agent completes a security scan → output contains findings referencing components
- An extraction step (Haiku-class model) identifies entities and relationships
- Entities are upserted (deduplicated) into the graph
- How does this integrate with our existing `KnowledgeExtractor` (ADR-014) that already extracts learnings?

**Q4: Context assembly integration**
How would graph-retrieved context fit into our existing `ContextAssembler` priority pipeline (ADR-010)?
- New priority layer? What priority level?
- Token budget allocation?
- When is graph context retrieved vs vector context? (both? conditional on task domain?)
- Show the `AIFunction` tool definition that agents would use: `TraverseKnowledgeGraph(startNode, relationship, maxDepth)`

**Q5: Hybrid queries (graph + vector)**
Can we combine graph traversal with vector similarity in a single query pipeline?
Example: "Find learnings similar to X that are connected to Component Y within 2 hops"
- Graph narrows the scope (connected to Y) → vector ranks by similarity
- How does this work with each option?

**Q6: Microsoft GraphRAG assessment**
Microsoft's GraphRAG library (github.com/microsoft/graphrag):
- Does it work with Claude (Anthropic) models, or is it OpenAI-only?
- Can it use PostgreSQL as its storage backend (vs default Parquet/CosmosDB)?
- What's the entity extraction → community detection → summarisation pipeline?
- Is this complementary to a hand-built knowledge graph, or a replacement?
- Production readiness: version, stability, breaking changes?

### Our Domain Graph Model (Draft)

For reference, these are the entity types and relationships we'd want to model:

```
(Policy)──[REQUIRES]──►(Control)──[EVIDENCED_BY]──►(Finding)
                                                        │
                                                   [PRODUCED_BY]
                                                        │
                                                        ▼
(Project)──[CONTAINS]──►(Component)──[DEPENDS_ON]──►(Library)
    │                       │                           │
    │                  [SCANNED_BY]                [HAS_VULNERABILITY]
    │                       │                           │
    │                       ▼                           ▼
    │                   (Agent)──[USES]──►(Tool)    (CVE)
    │
    [HAS_FINDING]──►(Finding)──[MAPS_TO]──►(OWASP_Category)
    │
    [ASSIGNED_TO]──►(Team/Member)
```

### Output Format

- Comparison table filled in with clear assessments
- Answer each Q1-Q6 with code where relevant (C# / SQL)
- **Recommendation**: which option for a regulated bank on Azure PG Flexible Server, with rationale
- **Migration path**: how to incrementally add graph capability without disrupting existing pgvector knowledge layer
- **Effort estimate**: relative complexity (small/medium/large) for each option

Keep total response under 3500 words. Tables, SQL, and C# code preferred over prose.

---

## After Research

Save claude.ai's response as: `docs/098-research/R18-response-graphrag-knowledge-graph.md`

Then, if the research validates a viable approach, produce: `docs/002-architecture/ADR-015-knowledge-graph.md`
