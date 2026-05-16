# Knowledge Graph Feasibility Assessment for Azure Database for PostgreSQL Flexible Server (PG 16/17) — May 2026

## TL;DR

- **Apache AGE is now Generally Available on Azure Database for PostgreSQL Flexible Server for PG 16, PG 17, and PG 18** (GA announcement and tech-community guidance confirm openCypher in-database alongside `pgvector`/`pg_diskann`). This is the recommended path: a single managed database, ACID transactions across SQL + Cypher, no sidecar to operate, and a working — if community-maintained — `Npgsql.Age` / `ApacheAGE` NuGet for C# / Microsoft Agent Framework.
- **Microsoft GraphRAG (currently in the v2.7.x line on GitHub, v3.0.x on PyPI) is Python-only, has no .NET SDK, has had repeated breaking changes, and has no first-class PostgreSQL backend** (LanceDB, Azure AI Search and Cosmos DB are the built-ins, with a documented Bring-Your-Own-Vector-Store factory). It is best treated as an *offline indexing pipeline* whose extracted graph and embeddings you ship into PG + AGE, not as a runtime library for a regulated .NET service. Since v2.7 it defaults to LiteLLM, which transparently supports Anthropic Claude (including the Azure Foundry Claude deployment).
- **For the stated workload (dependency chains, impact analysis, compliance traceability, 4–5-hop reasoning over 10k–100k nodes), the production-ready stack is: AGE for graph traversal + pgvector/DiskANN for similarity + recursive CTEs for simple hierarchies, all in the existing Azure PG Flexible Server, accessed from MAF via Npgsql.** A Neo4j sidecar (AuraDB on Azure Marketplace) is feasible but adds an ~$800–$1,500/month managed service, a CDC pipeline, and a second compliance perimeter — only justified if you need Neo4j GDS algorithms or Bloom visual tooling that AGE doesn’t match.

---

## Key Findings

### Q1 — Graph-capable extensions actually available on Azure PG Flexible Server

| Extension | Available on Azure PG Flex? | Purpose | Verdict for knowledge graph |
|---|---|---|---|
| **Apache AGE (`age`)** | **Yes — GA**, on PG 16, PG 17, PG 18 (blog “PostgreSQL as your Graph Database in the AI era”, May 2026; Microsoft Learn AGE overview) | True graph database — nodes/edges/properties stored in PostgreSQL, queried with openCypher | **Primary candidate.** Native multi-hop traversal, ACID with relational tables, BTREE/GIN indexes on properties. |
| **pgrouting** | Yes (in the official extensions list) | Geospatial routing (Dijkstra, A*, etc.) over edge tables | Not a knowledge-graph engine. Useful only if your “graph” is actually a routable network. |
| **pg_graphql** | **No — not in the Azure-allowlisted extensions list** (it’s a Supabase-developed extension) | GraphQL **API** layer that reflects SQL tables into a GraphQL schema | Even if available, **orthogonal** to graph traversal — it is a query *protocol*, not a graph engine. (Q6) |
| **pgvector** + **pg_diskann** | Yes (GA) | Vector similarity / ANN | Already in use; coexists with AGE. (Q7) |
| **azure_ai** | Yes | In-DB calls to Azure OpenAI / Foundry, including embeddings and now AI Functions (preview) | Pairs with AGE to do extraction → embed → store in one transaction. |

A March 2026 Microsoft Q&A thread (“Unable to enable Apache AGE extension on Azure PostgreSQL Flexible Server (PG 17.6) — InternalServerError”) shows that, between the late-2024 public preview and full GA, AGE on PG 17 required a maintenance roll-out. As of the May 2026 tech-community post, AGE is GA on PG 16/17/18 and the workaround (pinning to PG 16) is no longer required. Confirm with `SHOW azure.extensions;` in your target region before committing.

**Known operational caveat:** Microsoft documents that the in-place major-version upgrade feature does **not** support databases with AGE installed (along with `anon`, `dblink`, `orafce`, `postgres_fdw`, `timescaledb`). Plan major-version upgrades with dump/restore or logical replication.

### Q2 — Microsoft GraphRAG library assessment

- **Version status (May 2026).** GitHub `microsoft/graphrag` is on the v2.x line (v2.7.0 was tagged on 9 Oct 2025 with patches for LiteLLM defaults and Azure auth scope). PyPI lists a `graphrag 3.0.9` build. Both lines explicitly warn that the repo is *not an officially supported Microsoft offering* — it is research code with a published breaking-changes log that requires re-running `graphrag init --root . --force` between minor versions, and migration notebooks between majors. This is significant for a regulated financial-services delivery: it is not enterprise-supported SLA software.
- **Language / SDK surface.** Python-only. There is **no C#/.NET SDK or NuGet package** from Microsoft. .NET integration is therefore process-boundary (CLI/REST) only.
- **LLM provider support / Claude.** Since v2.7 GraphRAG defaults to **LiteLLM** for LLM access. LiteLLM has first-class support for `anthropic/...` models *and* for **Claude on Azure Foundry** (`azure_ai/claude-sonnet-4-5` against `https://<resource>.services.ai.azure.com/anthropic`). So GraphRAG can drive Claude either via Anthropic direct or via the Azure Foundry Claude deployment (Sonnet 4.5/4.6, Opus 4.5/4.7, Haiku 4.5 are GA on Foundry as of 2026). For a regulated bank, the Foundry path is preferable because it keeps inference inside the Azure compliance boundary.
- **PostgreSQL as a backend.** The built-in vector stores are **LanceDB (default, file-based Parquet/Arrow), Azure AI Search, and Cosmos DB**. There is no built-in PostgreSQL/pgvector store. However, GraphRAG exposes a `VectorStoreFactory` and an abstract `BaseVectorStore` ("Bring-Your-Own Vector Store" notebook in the official docs), so a `pgvector`-backed implementation is straightforward to write but you own and maintain it. Microsoft’s own “GraphRAG and PostgreSQL integration in docker with Cypher query and AI agents” reference architecture (Azure AI Foundry blog) demonstrates exactly this: GraphRAG indexes into Parquet, then loads the entity/edge/community tables into PG with AGE and `pgvector` to serve queries — i.e. **GraphRAG as a one-shot ETL pipeline, PG+AGE as the runtime store**.
- **Pipeline stages** (from the official GraphRAG documentation and DataFlow page):
  1. **TextUnit chunking** – split the corpus.
  2. **Entity, relationship, claims extraction** – LLM-driven, with a multi-stage self-reflection loop that asks the LLM whether more entities were missed.
  3. **Entity resolution / deduplication** – merge `"OpenAI"`, `"Open AI"`, `"the company"`.
  4. **Graph augmentation – hierarchical Leiden community detection** – produces a recursive community tree.
  5. **Community summarisation** – LLM summary per community at each hierarchy level.
  6. **Embedding** – text units, entities, community reports all embedded for retrieval.
  7. **Query** – Local Search (entity-centric), Global Search (community-summary map-reduce), and DRIFT (default since late-2024 — combines global + local for mixed-complexity queries).
- **Production readiness.** Breaking changes between minor versions, expensive indexing (Microsoft’s own warning; the cost-reduction work that produced LazyGraphRAG was explicitly motivated by ~$33k indexing bills on large corpora), and the “demonstration, not supported product” disclaimer make GraphRAG unsuitable as an embedded runtime dependency in a regulated C# service. It is a strong *offline knowledge-graph builder* whose outputs (entities, relationships, community reports, embeddings) are loaded into your supported store (PG + AGE + pgvector).

### Q3 — Apache AGE status

- **Latest stable version.** AGE 1.6.0 is the current release line (it is the version Microsoft documents in the AGE-on-Azure overview). Releases are Apache-cadence; the project upstream now claims support for PostgreSQL 11–18.
- **PG 16 / 17 compatibility.** Both work upstream and on Azure PG Flexible Server. Earlier GitHub issues (#2111 et al.) about PG 17 incompatibility predate the PG 17 build that Microsoft now ships.
- **.NET client.** No official C# driver exists upstream (apache/age#640 explicitly notes this), but two community packages on NuGet — `Konnektr.Npgsql.Age` (1.1.0, an `Npgsql` plugin that adds `dataSource.CreateCypherCommand(...)` and `Agtype` / `Vertex` types) and `ApacheAGE` (1.0.0) — let you submit parameterised openCypher from C# and read results as typed graph objects. Treat them as community-supported; vet for your regulatory posture before adoption.
- **Performance characteristics.** AGE uses PostgreSQL’s storage and indexes — vertex IDs, edge `start_id`/`end_id` and `properties` columns are indexed with BTREE for equality/range and **GIN on the JSONB-like `agtype` `properties` column**; expression BTREE indexes on specific property keys are recommended (Microsoft Learn “Apache AGE Performance Best Practices”). For 10k–100k nodes and depth-4–5 traversals you are comfortably inside AGE’s sweet spot when (a) `start_id` and `end_id` on every edge label have BTREE indexes, (b) property predicates use expression indexes, and (c) you cast `agtype` back to PG types for joins to relational tables. Note an active GitHub issue (apache/age#1522) where the planner did not always pick property indexes for unconstrained `MATCH ... ORDER BY` queries — push selective `WHERE` clauses into the Cypher fragment and verify with `EXPLAIN`.
- **Coexistence with pgvector.** Yes — AGE installs into its own `ag_catalog` schema and uses no name collisions; the official AGE FAQ confirms it can coexist with other PostgreSQL extensions, and Microsoft’s reference architectures explicitly combine AGE with pgvector and DiskANN in the same database.
- **Fallback if AGE were unavailable.** AGE runs on any Postgres in an Azure VM or container (build from source against PG 16/17, or use the `apache/age` Docker image), but that gives up the Flexible Server managed control plane (HA, PITR, Entra ID, automated patching) you presumably picked Flex Server for in the first place. With AGE now GA on Flex Server, this is purely a theoretical fallback.

### Q4 — Neo4j on Azure as a sidecar option

- **Hosting options.** Three realistic paths: **Neo4j AuraDB** (managed) via the Azure Marketplace; **self-managed Neo4j Enterprise** on Azure VMs or AKS; or community Docker images. Note that Neo4j relaunched the Azure Marketplace listing on 13 January 2026 — pre-Jan-2026 marketplace subscribers are on a "legacy" SKU that only includes AuraDB Professional; **AuraDB Business Critical, Aura Graph Analytics and other features require subscribing to the new listing**.
- **Pricing (list, May 2026).** AuraDB Professional starts at roughly $65/month for the smallest instance; **typical mid-size production (≈32 GB memory) lists at $800–$1,500/month**; large deployments reach $5,000–$15,000+ monthly. Self-managed Neo4j Enterprise annual subscriptions range from ~$15k for small footprints to $100k+ for multi-data-centre. (Sources: Neo4j’s own pricing page; Vendr 2026 Neo4j pricing analysis; AuraDB Professional Azure Marketplace listing.)
- **.NET driver.** `Neo4j.Driver` 6.0.0 (NuGet, January 2026 release) is the current official Bolt driver. It is `IAsyncDisposable`-friendly, exposes `IDriver`, `IAsyncSession`, `ExecutableQuery` and an `AsObjectsAsync<T>()` mapper. It is *not* an EF Core provider — Neo4j is not relationally modelled and there is no first-party EF Core integration. Object-graph mappers like `Neo4jClient` (3rd-party) and `Neo4j.Driver.Extensions` exist but are community-maintained.
- **Sync patterns.** The accepted pattern is **Debezium → Kafka → Neo4j Streams** (or a custom sink), reading the PG WAL via logical replication and projecting CDC events into Cypher MERGE statements. This adds a Kafka/Confluent component, schema-evolution discipline, and a *second* point of compliance evidence (lineage, retention, ACLs on the event stream).
- **Operational complexity for a regulated workload.** Expect: a second managed service in your subscription with its own RBAC / Entra-ID story, a CDC stack to operate, double the backup / DR / patching coordination, more cross-system audit trail to reconcile, and divergence windows where PG and Neo4j temporarily disagree (a known issue when answering compliance questions). For most “dependency chain / impact analysis / multi-hop” workloads at the 10k–100k-node scale described, this complexity is hard to justify against an in-database AGE solution — *unless* you need Neo4j Graph Data Science algorithms (centrality, embeddings, link prediction) or Bloom-style visualisation tooling, where Neo4j genuinely outclasses AGE today.
- **Neo4j + Microsoft Agent Framework.** Neo4j’s December 2025 developer post “Empowering Microsoft Agent Framework with Neo4j Knowledge Graphs” documents four integration patterns: (1) Context Providers that inject graph context into every LLM call, (2) direct SDK tools wrapping the .NET driver, (3) MCP servers exposing Neo4j to multiple agents, (4) HTTP/REST. All four work in C# / MAF; the SDK + MCP patterns are most idiomatic.

### Q5 — Pure-PostgreSQL recursive CTE performance

- **Mechanics.** `WITH RECURSIVE` is implemented iteratively (anchor → working table → recursive term → intermediate → swap), per the PostgreSQL 18 documentation. PostgreSQL 14+ adds `SEARCH BREADTH FIRST / DEPTH FIRST BY ... SET ...` and `CYCLE` clauses that materialise traversal order and cycle detection without hand-rolled path arrays — relevant for compliance traceability where audit paths matter.
- **PG 17 / 18 relevance.** PostgreSQL 17 includes general CTE planner improvements and better `IN`/B-tree index selection that benefit recursive-CTE-style workloads, although there is no headline “graph traversal” feature. Materialisation behaviour was already tunable from PG 12 (`AS MATERIALIZED` / `NOT MATERIALIZED`).
- **Indexing for adjacency-list traversal.** The non-negotiables are: a BTREE on the parent column (`parent_id`, or whatever points "up" the chain), a BTREE on the child column for downward traversal, covering indexes if you need to avoid the heap, and a `LIMIT` or depth-bound predicate in the recursive term to prevent runaway recursion on cycles. For deep static hierarchies, the `ltree` extension (available on Azure PG Flex) with a GiST index on the `ltree` path materially outperforms recursive CTEs for ancestor/descendant lookups, at the cost of maintenance on updates.
- **Empirical performance.** Published benchmarks (Cybertec, Medium "Postgres Recursive Query(CTE) or Recursive Function?", Yugabyte) consistently show that on small adjacency tables (~500 rows) recursive CTEs answer in milliseconds, but for graphs with branching factor 5–10 at depth 4–5 over 10k–100k nodes they degrade super-linearly. A widely-cited contrived test showed a 500-row tree taking 1.26 s with a recursive CTE due to repeated sequential scans, because the recursive term issued one scan per loop iteration. Indexing fixes most of that, but the fundamental cost model is "one iteration per depth level" and the planner cannot push predicates across the recursion boundary.
- **Limitations vs. native graph languages.** Recursive CTEs cannot express variable-length path matching (`(a)-[*1..5]->(b)`), shortest-path, or pattern-matching beyond what is encoded by hand; cycle detection is a manual `path` array; and the optimisation fence around CTEs historically limited what the planner could rewrite (improved in recent PG releases but not eliminated). An arxiv paper on graph algorithm support concludes that recursive CTEs "were challenging to implement and performed poorly" compared to iterative loops materialising intermediate tables — i.e. for serious graph algorithms, recursive CTEs are not competitive with AGE or Neo4j.
- **Bottom line.** Use recursive CTEs for *known-shape* hierarchical queries (org charts, single parent chains, ≤3 hops with a tight `WHERE`) and `ltree` for static taxonomies; do **not** rely on recursive CTEs for impact-analysis or compliance-traceability traversals that vary in depth or shape at query time.

### Q6 — pg_graphql

- **What it is.** A Postgres extension developed by Supabase that **reflects your existing SQL schema as a GraphQL schema** and exposes it via a single SQL function `graphql.resolve(...)`. Tables become collection types, foreign keys become relationship fields. It is a **GraphQL API layer** — a *protocol*, not a graph engine.
- **Availability on Azure.** **Not listed** in the Azure Database for PostgreSQL Flexible Server allowlist. (The Azure-supported list is the union published at *learn.microsoft.com/.../extensions/concepts-extensions-versions* and confirmed in Microsoft’s "Considerations" page; `pg_graphql` does not appear.)
- **Does it solve graph traversal?** No. Resolving `customer { orders { lineItems { product { } } } }` becomes nested SQL joins; the extension does not give you variable-length paths, shortest path, community detection or anything topologically graph-shaped. It is **orthogonal** to AGE, and entirely unrelated to your dependency-chain / impact-analysis problem.

### Q7 — pg_diskann + pgvector + graph hybrid

- **Coexistence.** `pg_diskann` is GA on Azure PG Flex (per the September 2024 feature recap and the 2026 Microsoft Learn pages); it depends on `pgvector` and is fully compatible — same `vector` data type, same distance operators (`<=>`, `<->`, `<#>`), same index DDL surface (`USING diskann (embedding vector_cosine_ops)`). Critically for regulated workloads, **DiskANN’s Advanced Filtering** combines filter predicates and graph (Vamana) traversal into one operation, fixing the long-standing pgvector HNSW issue where filtered queries silently returned too few results. PQ (product quantization) since `pg_diskann` 0.6 keeps memory in check on >1M-row corpora.
- **Hybrid query patterns with AGE.** Three standard patterns work well in PG + AGE + DiskANN:
  1. **Vector → Graph (entity grounding)** — DiskANN-rank the top-K embeddings, extract their entity IDs, then `MATCH (e {id: ...})-[*1..N]-(neighbour) RETURN ...` in Cypher to expand the neighbourhood.
  2. **Graph → Vector (path-filtered similarity)** — Cypher computes a relevant subgraph (e.g. "all entities within 3 hops of this regulation"), then a `WHERE entity_id = ANY($subgraph)` clause filters the vector search.
  3. **Reranking** — DiskANN over-fetches (top 50), Cypher computes path-based features, an outer query reorders by a composite score; the Microsoft `learn.microsoft.com/.../how-to-use-pgdiskann` page documents this two-step reranking idiom.
- **Operational note.** A vector index and a graph traversal share the same shared_buffers and IOPS budget. Plan storage and `work_mem` for the larger of the two; the AGE GIN-on-properties index, in particular, can balloon if `properties` JSON is large.

### Q8 — EF Core and graph data

- **No native graph data model in EF Core.** EF Core models a tabular world. The closest first-party feature is SQL Server's `HierarchyId` via the `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId` package — **SQL Server only, not PostgreSQL**.
- **Recursive CTEs from EF Core.** The supported pattern is `FromSqlRaw` / `FromSqlInterpolated` on a `DbSet<T>` (or a keyless entity / `DbSet` projection), with the recursive CTE inlined and parameters passed safely. EF Core *cannot* compose additional LINQ on top of a non-composable raw SQL CTE — adding `.Where(...)` after `FromSqlRaw` throws `InvalidOperationException: 'FromSqlRaw' was called with non-composable SQL`. Workarounds: (a) `AsEnumerable()` before further filtering (client-side, fine for small results), or (b) wrap the CTE in a SQL view or function and `FromSqlRaw` against the view, which restores composability. Khalid Abuhakmeh’s widely-referenced 2021 post and the 2024–2026 follow-ups document the pattern; a peer-reviewed BTW 2023 study comparing five recursive-relationship strategies in EF Core concluded that **recursive CTEs and "key loading" (BFS materialised in code) are the only consistently performant options**.
- **Pure-LINQ recursion.** Doesn’t exist for arbitrary depth. The "max-depth projection" pattern (Michael Ceber, Medium) emits nested `LEFT JOIN`s up to a known depth, useful only when you can bound the depth at compile time.
- **For your stack (MAF + Npgsql + AGE).** EF Core is a poor fit for the graph layer specifically. The cleanest model is:
  - **Relational entities** (users, vendors, contracts, controls) → EF Core as today.
  - **Graph traversals** (Cypher) → raw `NpgsqlCommand` / `dataSource.CreateCypherCommand(...)` using `Npgsql.Age`, returning `Agtype`/`Vertex`/`Edge` typed results; or `DbContext.Database.GetDbConnection()` to share the same connection/transaction.
  - **Recursive hierarchical queries** that don’t need Cypher → `FromSqlRaw` on a keyless entity mapped to a SQL view that hosts the recursive CTE.
- This split keeps EF Core where it shines (typed CRUD, change tracking, migrations) and uses purpose-built tooling for traversal — exactly the integration shape Microsoft’s own "Building Blocks for AI in .NET Part 3" post recommends for MAF agents that need a `VectorData` store *plus* domain data.

---

## Details

### Recommended architecture

For a financial-services platform on Azure PG Flexible Server with MAF in C#, the lowest-risk path that meets all four stated needs (dependency chains, impact analysis, compliance traceability, multi-hop reasoning) is:

1. **Storage layer (single PG Flex instance):**
   - Relational tables (existing) for first-class entities.
   - `pgvector` 0.7+ with `pg_diskann` (already in place) for 1536-d cosine similarity, with DiskANN Advanced Filtering for any filtered vector queries.
   - **`age` extension enabled** with `shared_preload_libraries = ... ,age`; one or more `ag_catalog.create_graph('compliance_kg')` graphs. Vertex labels by entity type (`Control`, `Regulation`, `Process`, `Vendor`, `Contract`, `System`...), edge labels by relationship type (`GOVERNS`, `DEPENDS_ON`, `OWNED_BY`, `IMPLEMENTS`...). BTREE on every label's `id`, `start_id`, `end_id`; GIN on `properties`; expression BTREE on the property keys you filter on hot paths.

2. **Graph construction:**
   - **Online** (transactional writes): MAF agents call C# repositories that issue `INSERT`s for relational rows *and* Cypher `MERGE`/`CREATE` for graph nodes/edges, **inside one PG transaction** — AGE is fully ACID with the rest of the database. This is the regulatory differentiator versus a Neo4j sidecar.
   - **Bulk** (initial load / periodic re-extraction from unstructured docs): Run **Microsoft GraphRAG as an offline batch job** (Python, possibly on Azure Container Apps or AKS), pointed at Azure Foundry Claude via LiteLLM for entity/relationship extraction and community summarisation, with `azure_ai` extension or `text-embedding-3-small` (1536-d, matching the existing pgvector schema). Materialise GraphRAG’s `entities.parquet`, `relationships.parquet`, `communities.parquet`, `text_units.parquet`, `community_reports.parquet` into PG tables, then bulk-translate into AGE vertex/edge tables. The "GraphRAG and PostgreSQL integration in docker" Microsoft Community Hub post is a working reference for exactly this pattern.

3. **Query layer from C# / MAF:**
   - `Npgsql` (already in use) with `Npgsql.Age` plug-in to issue parameterised Cypher: `MATCH (c:Control {id: $id})-[:DEPENDS_ON*1..5]->(d) RETURN d`.
   - Recursive CTEs via `FromSqlRaw` over a SQL view for bounded-shape relational hierarchies (org chart, account tree).
   - DiskANN-indexed `ORDER BY embedding <=> $1::vector LIMIT k` for similarity, optionally fenced by a Cypher-derived ID set.
   - Compose all three in a single SQL transaction when the answer depends on consistency.
   - Expose the graph + vector capabilities as MAF tools (`AIFunction`-decorated methods) and/or via an MCP server using the Microsoft `mcp-server-postgresql` / Azure PostgreSQL MCP server (public preview per the 2026 Azure release notes), so agents can `query_graph(...)`, `vector_search(...)`, and `traverse(...)` without prompt-engineering raw Cypher.

4. **Observability & compliance:**
   - Single audit log (PG `pg_audit` + Query Store + Microsoft Entra ID auth), single PITR window, single backup geo-redundancy story. Versus a Neo4j sidecar this is the largest single risk reduction.

### When you should still add Neo4j

- You need GDS algorithms (Louvain/Leiden at scale, Node2Vec, link prediction, betweenness centrality on >1M nodes) that AGE does not ship.
- You need Bloom or NeoDash-style visual exploration for non-developer auditors.
- You expect the graph to exceed several million nodes / tens of millions of edges and traversals at depth 6+ where AGE’s heap/index access pattern starts to underperform a native graph engine.
- In those cases, run AuraDB on the new Azure Marketplace listing, drive it from C# via `Neo4j.Driver` 6.0.0, and CDC from PG using Debezium + a sink. Budget USD ~$1k/mo at the low end of "production" and a non-trivial DevOps spend on the CDC pipeline.

### GraphRAG pipeline summary (for your assessment doc)

| Stage | Engine | Output |
|---|---|---|
| Chunking | Python | `text_units` |
| Entity / Relationship / Claim extraction | LLM (Foundry Claude or Azure OpenAI via LiteLLM) | `entities`, `relationships`, `claims` |
| Entity resolution | LLM-assisted, deterministic merge | de-duplicated entities |
| Graph augmentation | Hierarchical Leiden | community hierarchy |
| Community summarisation | LLM | `community_reports` |
| Embedding | Embedding model (1536-d to match existing pgvector) | vectors for text units, entities, reports |
| Query — Local Search | entity-anchored | entity-neighbourhood answer |
| Query — Global Search | community-summary map-reduce | corpus-level answer |
| Query — DRIFT (default) | hybrid local+global | mixed-complexity answer |

### What we did *not* find evidence for

- **No first-party Microsoft .NET SDK for GraphRAG** (confirmed by repo inspection and the breaking-changes / CHANGELOG files).
- **No EF Core PostgreSQL `HierarchyId` equivalent.** SQL Server only.
- **No official Microsoft support statement for `pg_graphql` on Azure PG Flexible Server.**
- **No published Microsoft benchmark of AGE at 10k–100k nodes / depth 4–5** specifically — the AGE performance guidance is qualitative (indexing recommendations, EXPLAIN-first guidance). A short PoC on representative data is required before sizing decisions.

---

## Caveats

- **AGE on PG 17 was unstable as recently as March 2026** (the Microsoft Q&A "InternalServerError" thread) before a backend maintenance roll-out. Validate on your target region/server and confirm the minor version in `SELECT version()` is at or above the level Microsoft documents as "AGE GA on PG 17". If your region has not received the May 2026 update, pin new servers to PG 16 for now and plan the PG 17 move once GA is confirmed for your region.
- **AGE blocks Azure’s in-place major-version upgrade feature.** Treat AGE-enabled servers like Timescale-enabled servers: plan upgrades via logical replication or dump/restore.
- **Microsoft GraphRAG is "demonstration code, not an officially supported Microsoft offering"** — that disclaimer is unchanged on the repo as of May 2026 and matters for procurement/SOC-2/SR-11-7 review in financial services. Treat its outputs (Parquet artefacts) as ingested data, not as a runtime dependency in your service.
- **Community NuGet packages (`Npgsql.Age`, `ApacheAGE`)** are not Microsoft- or Apache-published; vet supply-chain, signing and licence (Apache-2.0, but distributed via Konnektr / Hallixon respectively) before adopting in production. Consider vendoring the source.
- **DiskANN is currently Azure-only.** If portability off Azure ever becomes a requirement, your retrieval stack would have to fall back to pgvector HNSW or IVFFlat with the recall/filtering trade-offs documented above.
- **Apache AGE upstream version pace lags PG releases** — the repo claims PG 11–18 support but historically major-version support has trailed by months. Azure manages this for you on Flex Server, but self-hosted AGE on a VM/container is your problem to keep in sync.
- **AuraDB pricing figures cited** are list prices from Neo4j’s pricing page and Vendr’s 2026 analysis; actual enterprise contracts typically settle 15–30% below list, and the Azure Marketplace SKU mix changed on 13 January 2026 — re-quote with your Microsoft account team rather than relying on these numbers.
- **PG 17/18 recursive-CTE improvements are general planner improvements**, not graph-specific. They do not change the fundamental scaling limits of recursive CTEs for variable-depth pathfinding; treat them as a nice-to-have, not a substitute for AGE.
- **A short load-representative PoC is essential** before committing to AGE for 10k–100k nodes at depth 4–5. Published AGE benchmarks at this scale are sparse and dataset-shape-dependent (degree distribution, predicate selectivity, JSONB property size all dominate runtime). Reserve 1–2 sprints to run your real dependency-chain queries on a copy of the production data with the recommended index set (BTREE on `id`/`start_id`/`end_id`, GIN on `properties`, expression indexes on hot keys) and verify p95 latency with `EXPLAIN (ANALYZE, BUFFERS)`.
- **Some prospective tech mentioned in passing** (Azure HorizonDB announced at Ignite 2025 as Microsoft’s shared-storage Postgres-compatible service competing with Aurora; AI Functions in `ai_extension` in public preview; MCP Server for Azure PostgreSQL in public preview) is **not yet GA** as of May 2026; treat these as roadmap, not decisions.