# Data Layer Architecture for a .NET 9 AI Agent Orchestration Platform on Azure (Regulated Bank, South Africa North) — May 2026

## TL;DR
- **Recommended primary architecture: Option A (PostgreSQL Flexible Server + pgvector) for the OLTP / vector workload, with the LLM call audit table moved to a partitioned table inside the same PostgreSQL server (range-by-time + hash-by-tenant).** It is the only option that satisfies *all* of: 34 FK-rich entities with cascading deletes, EF Core 9 LINQ + JSON/jsonb, native UUIDv7 generation, 1536-dim cosine similarity with HNSW or DiskANN, and full availability with SOC 1/2/3, ISO 27001, PCI DSS L1, PITR and zone-redundant HA in **South Africa North** today.
- **Option D (Cosmos DB for PostgreSQL / Citus) is effectively eliminated**: Microsoft has placed it on a retirement path (no new projects recommended) and is steering customers to *Azure Database for PostgreSQL **Elastic Clusters***. Even apart from that, distributed Citus has well-known limitations with cross-shard FK cascades and EF Core change-tracking that are a poor fit for a 34-entity relational model. **Option B** (Azure SQL + Azure AI Search) is the second-best option but adds two products, ~$74+/month per Search Unit, and Azure SQL's DiskANN vector index is still preview with patchy regional rollout. **Option C** (Postgres + Cosmos DB NoSQL split) is over-engineered for 10k–100k embeddings and breaks transactional integrity between agents and their embeddings.
- **Key hard facts (May 2026):** `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.x targets EF Core 9 / .NET 9; `Pgvector.EntityFrameworkCore` 0.3.0 supports EF Core 9 *and* 10. Azure PostgreSQL Flexible Server runs **pgvector 0.8.0 GA** with HNSW + iterative scan for filtered hybrid search and has the optional **`pg_diskann`** extension. Flexible Server is available in South Africa North with 3 AZs and zone-redundant HA. Azure SQL's native `VECTOR` type is GA in all regions (June 2025), but `CREATE VECTOR INDEX` / `VECTOR_SEARCH` (DiskANN) is still rolled out as a preview region-by-region. EF Core 9 needs the `EFCore.SqlServer.VectorSearch` plugin for SQL vector LINQ; EF Core 10 has it built-in.

---

## Key Findings

### 1. Comparison Matrix

| Criterion | A: PG Flex + pgvector | B: Azure SQL + AI Search | C: PG + Cosmos DB (NoSQL) split | D: Cosmos DB for PG (Citus) |
|---|---|---|---|---|
| **EF Core 9 / .NET 9 maturity** | ★★★★★ Npgsql.EFCore.PostgreSQL 9.0.x is GA, fully tracks EF Core 9 features (complex types, ExecuteUpdate on JSON, UUIDv7 default) | ★★★★★ EF Core 9 SqlServer provider; vector LINQ requires `EFCore.SqlServer.VectorSearch 9.0.0-preview.x` plugin; EF Core 10 has it built-in | ★★★★ PG side same as A; Cosmos provider in EF Core 9 was largely rewritten (better JSON, point reads, hierarchical PKs) | ★★★ Same Npgsql provider, but Citus distributed-table semantics break some EF behaviours (multi-shard joins, FK cascades across shards) |
| **JSON column support (ToJson, OwnsMany, jsonb)** | ★★★★★ Native `jsonb`; `ToJson()` (EF 8+) works with `OwnsOne`/`OwnsMany`; LINQ traversal compiles to `->>`, `#>>`, `jsonb_array_elements_text`. Can also map a CLR property directly to `jsonb`. | ★★★★ EF Core 9 `ToJson()` mapping over `nvarchar(max)`; query support improved but less rich than PG `jsonb` operators (no GIN indexing) | ★★★★ For PG; Cosmos is JSON-native but not jsonb operators | ★★★★ Same as A but JSONB across distributed tables works with caveats |
| **Vector search at 10k–100k (1536-dim cosine)** | ★★★★★ pgvector 0.8.0 with HNSW (m=16, ef_construction=64) easily handles 100k×1536 with sub-50 ms p95; optional DiskANN via `pg_diskann` extension for billion-scale | ★★★★ Azure SQL native `VECTOR(1536)` GA; exact `VECTOR_DISTANCE` works in every region; **DiskANN `VECTOR_SEARCH` index is preview** with regional rollout (Central US, UK South, North Europe confirmed; South Africa North: not currently listed). Outside Azure SQL: AI Search HNSW is excellent | ★★★★★ Azure AI Search HNSW + semantic ranker handles tens of millions; for 100k vectors it is over-spec'd | ★★★★ Same as A, but on a more expensive substrate |
| **Hybrid search (filter + vector + rerank)** | ★★★★ pgvector 0.8 *iterative_scan* prevents over-filtering when WHERE predicates eliminate HNSW candidates; combine with full-text `tsvector` and BM25 in SQL; no built-in semantic reranker | ★★★★★ AI Search has the most mature hybrid stack: BM25 + vector + RRF + transformer-based **semantic ranker** + agentic retrieval; default recommendation `vector_semantic_hybrid` on Standard tier | ★★★★★ Same as B (AI Search) | ★★★★ Same as A |
| **Cascading FK deletes (34 entities)** | ★★★★★ Native PostgreSQL `ON DELETE CASCADE`; EF Core `OnDelete(DeleteBehavior.Cascade)` works as expected | ★★★★★ Same in T-SQL | ★★★ Cosmos has no FKs; you split your model and lose referential integrity for anything that crosses the boundary | ★★ Citus distributed tables impose colocation constraints; cross-shard FKs require both tables to share the distribution column. 34 entities almost certainly won't all colocate cleanly |
| **High-volume write throughput (LLM audit)** | ★★★★ Single PG instance can sustain 50–150k inserts/s on declarative monthly RANGE partitions with BRIN on time + partial b-trees per tenant | ★★★★ Azure SQL Hyperscale is excellent; partitioned columnstore for audit | ★★★★★ Cosmos DB / ADX scale linearly; ADX = millions of events/s | ★★★★★ Citus distributes writes across worker nodes |
| **Aggregation queries (compliance)** | ★★★★ PG with partition pruning and `pg_stat_statements`; for very large analytics consider tiered archive to ADX | ★★★★ Columnstore index; great BI integration | ★★★★ ADX/Kusto is purpose-built; KQL `summarize`, anomaly detection | ★★★★★ Citus parallelizes aggregations across workers |
| **Azure managed availability** | ★★★★★ Flexible Server GA, fully managed | ★★★★★ Azure SQL DB and AI Search both GA | ★★★★★ Both GA | ★★ **On retirement path** — Microsoft explicitly says "no longer recommended for new projects"; migration tool to Elastic Clusters published April 2025 |
| **South Africa North availability** | ✅ Flexible Server GA in SA North with **3 availability zones**, zone-redundant HA, geo-redundant backup, geo-replicated read replicas; pgvector and pg_diskann available; PG 16/17/18 with in-place MVU | ✅ Azure SQL DB in SA North with 80-vCore SKU and AZ support; **VECTOR data type GA**; DiskANN vector index preview availability *not yet confirmed* in SA North per Microsoft regional matrix (confirmed in North Europe, UK South, Central US). AI Search: ✅ in SA North (also one of the regions with semantic-ranker free tier) | ✅ both available | ✅ available, but irrelevant given retirement |
| **Operational complexity** | ★★★★★ One database, one connection string, one backup story | ★★★ Two services to provision, secure, network-isolate, sync (CDC or app-level dual-write), audit | ★★ Two heterogeneous systems with different consistency, RU vs vCore billing, identity, networking | ★★★ Citus introduces shard-rebalance, distribution-key choice, reference-table strategy |
| **Cost (10k–100k vectors, ~5–20 GB DB)** | $ Cheapest. A GP_Standard_D4ds_v5 with HA in SA North ≈ ~$520/mo; storage and backup add modest amounts | $$ Azure SQL S2/Hyperscale + AI Search Basic (~$73/mo) or S1 (~$74/mo per Search Unit; +$ for semantic ranker queries beyond 1k/mo free) | $$$ Cosmos DB RU/s billing on top of PG | $$$ Citus needs at least 1 coordinator + 2 worker nodes; Elastic Clusters drops the coordinator surcharge but still ≥3 nodes |
| **Migration from existing PostgreSQL schema** | ★★★★★ Trivial: pg_dump/pg_restore or `azcopy` + Azure DMS | ★★ Requires schema rewrite (PostgreSQL → T-SQL types, citext, arrays, jsonb → nvarchar(max), sequences, FK semantics) | ★★★★ For the relational parts; Cosmos parts are net-new modelling | ★★★★ Same DDL, but you must add `SELECT create_distributed_table(...)` and pick a distribution column for every table; cross-shard FKs require schema changes |
| **Encryption at rest** | ✅ AES-256 at rest by default; **CMK with Key Vault** supported | ✅ TDE + CMK | ✅ Both | ✅ CMK only at create time |
| **Audit logging** | ✅ pgAudit extension allowlisted; integrates with Azure Monitor + Log Analytics | ✅ SQL Audit + Defender for SQL | ✅ Both have native audit | ✅ pgAudit GA |
| **PITR** | ✅ 1–35 days, geo-restore in paired region (UK South for SA West / SA North) | ✅ 1–35 days, LTR up to 10 yrs | ✅ Both | ✅ |
| **Banking compliance** | ✅ SOC 1/2/3, ISO 27001/27017/27018, PCI DSS L1, HIPAA, CSA STAR, FedRAMP. Maps to Azure Policy regulatory compliance built-ins for **PCI DSS v4.0**, **RBI**, **ISO 27001:2013** | ✅ Same Azure SQL DB compliance footprint, plus first-class Azure Policy regulatory baselines | ✅ Both compliant | ✅ Compliant but service is retiring |

---

### 2. EF Core 9 + Npgsql + pgvector — Concrete Setup (Option A)

**NuGet packages (verified May 2026):**

```xml
<ItemGroup>
  <!-- EF Core 9 / .NET 9 PostgreSQL provider -->
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />

  <!-- pgvector EF Core integration; 0.3.0 supports EF Core 9 AND 10
       (use 0.2.2 for EF Core 8) -->
  <PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />

  <!-- Tooling -->
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.5">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
```

`Pgvector.EntityFrameworkCore` transitively pulls in `Pgvector` (0.3.2) and `Npgsql` (≥9.0.2, which contains the SSL validation fix CVE-style hardening). All Microsoft `Microsoft.EntityFrameworkCore.*` packages must stay on the same 9.0.x line as the Npgsql provider.

**Working DbContext / model (.NET 9, EF Core 9):**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;

public sealed class Mission
{
    public Guid Id { get; set; }                       // UUIDv7, generated client-side by EFCore.PG
    public string Name { get; set; } = "";
    public uint Version { get; set; }                  // optimistic concurrency token (xmin)
    public MissionMetadata Metadata { get; set; } = new();   // -> jsonb
    public List<MissionEvent> Events { get; set; } = new();  // FK + cascade
}

public sealed class MissionMetadata                    // mapped to a single jsonb column via ToJson()
{
    public string OwnerTeam { get; set; } = "";
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
}

public sealed class MissionEvent
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public Mission Mission { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";    // raw jsonb
}

public sealed class KnowledgeChunk
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public string Content { get; set; } = "";

    // 1536-dim OpenAI text-embedding-3-small vector
    [Column(TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }
}

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> opts) : DbContext(opts)
{
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<MissionEvent> MissionEvents => Set<MissionEvent>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector");           // pgvector
        b.HasPostgresExtension("uuid-ossp");

        b.Entity<Mission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");  // or rely on EF UUIDv7
            e.Property(x => x.Version).IsRowVersion();                     // xmin concurrency token
            e.ComplexProperty(x => x.Metadata, m => m.ToJson("metadata")); // EF 9 complex type to jsonb
            e.HasMany(x => x.Events)
             .WithOne(x => x.Mission)
             .HasForeignKey(x => x.MissionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MissionEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.MissionId, x.OccurredAt });
        });

        b.Entity<KnowledgeChunk>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Embedding!)
             .HasMethod("hnsw")
             .HasOperators("vector_cosine_ops")     // cosine for OpenAI embeddings
             .HasStorageParameter("m", 16)
             .HasStorageParameter("ef_construction", 64);
        });
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextPool<AgentDbContext>(opts =>
    opts.UseNpgsql(
        builder.Configuration.GetConnectionString("Agent"),
        npg => npg
            .SetPostgresVersion(16, 0)              // pin to PG 16 (or 17/18) for stable SQL gen
            .UseVector()                            // enables pgvector ADO + EF mappings
            .EnableRetryOnFailure()));
```

**Querying — top-K cosine similarity with EF Core LINQ:**

```csharp
public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
    AgentDbContext db, Guid missionId, float[] queryEmbedding, int k = 10, CancellationToken ct = default)
{
    var q = new Vector(queryEmbedding);             // Pgvector type
    return await db.KnowledgeChunks
        .Where(c => c.MissionId == missionId)        // structured filter
        .OrderBy(c => c.Embedding!.CosineDistance(q))// translates to: embedding <=> @p
        .Take(k)
        .AsNoTracking()
        .ToListAsync(ct);
}
```

`Pgvector.EntityFrameworkCore` exposes `L2Distance`, `CosineDistance`, `MaxInnerProduct`, `L1Distance`, `HammingDistance`, `JaccardDistance` as LINQ-translated methods (compiled to the `<->`, `<=>`, `<#>`, `<+>`, `<~>`, `<%>` operators respectively). With pgvector 0.8 enable iterative scan to avoid filter-induced under-recall:

```csharp
await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = strict_order;", ct);
```

**UUIDv7 primary keys:** since `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0, EF generates UUIDv7 client-side by default for `Guid` keys (the value generator is now public as `NpgsqlSequentialGuidValueGenerator`). On PostgreSQL 18, configuring `SetPostgresVersion(18,0)` makes `Guid.CreateVersion7()` translate to PG's built-in `uuidv7()`. Either way you get index-friendly time-ordered keys without writing custom code.

**Optimistic concurrency:** the recommended PG idiom is `IsRowVersion()` against the system column `xmin` (treated as a `uint` token). EF generates `WHERE id = @id AND xmin = @ver` automatically and throws `DbUpdateConcurrencyException` on conflict.

---

### 3. Azure AI Search vs pgvector — Is AI Search overkill at 10k–100k?

For *this* workload (10k–100k OpenAI 1536-dim embeddings, predominantly per-mission/per-tenant filtering, single-region), **Azure AI Search is mostly overkill.** What it adds beyond pgvector:

| Capability | pgvector 0.8 (Flex) | Azure AI Search Standard |
|---|---|---|
| HNSW + cosine | ✅ | ✅ |
| Iterative / filtered ANN | ✅ since 0.8 (`hnsw.iterative_scan`) | ✅ (post-filter, pre-filter, RRF) |
| BM25 keyword fusion | Manual via `tsvector` + RRF SQL | ✅ first-class hybrid (`vector_semantic_hybrid`) |
| **Cross-encoder semantic reranker** | ❌ (would need a separate cross-encoder service) | ✅ (Microsoft transformer rerank, top-50 → reranked) |
| Agentic retrieval / query planning with conversation memory | ❌ | ✅ |
| Geographic / spatial + facets together with vector | ✅ via PostGIS | ✅ |
| Skillset enrichment (OCR, KV extraction) at index time | ❌ | ✅ |
| Cost floor | ~0 incremental on existing PG | Basic ≈ included tier; **S1 Search Unit ≈ $73.73/mo**, semantic ranker after 1k/mo free queries: paid plan |
| Operational story | one DB | extra service, separate auth, separate VNet, dual-write or indexer pipeline |

**Recommendation:** start with pgvector. Only add AI Search if (a) you genuinely need the cross-encoder semantic reranker for end-user-facing RAG quality, (b) the corpus grows past ~1M chunks, or (c) you adopt agentic retrieval / Foundry IQ. Microsoft's own guidance: "Start with `vector_semantic_hybrid` on Standard tier" applies *if* you already need AI Search; it does not say you should use AI Search at all costs.

---

### 4. LLM Call Audit Table — Where Should It Live?

Workload: tens to hundreds of millions of rows/year, append-only, dimensional aggregations (cost by tenant/model/day, p95 latency, token counts).

| Option | Verdict |
|---|---|
| **Same PostgreSQL, plain table** | Fine up to ~50M rows, then bloat + vacuum pain |
| **Same PostgreSQL, native declarative partitioning (RANGE by month + optional HASH by tenant)** | **Recommended.** Partition pruning keeps queries flat; `DROP PARTITION` for retention is instant; BRIN index on `created_at` keeps writes cheap. Same connection, same auth, same backups, same compliance scope. Aha! and others have shown this scales to billions of rows on RDS-class hardware. |
| **Cosmos DB NoSQL** | Linear write scaling and global distribution, but RU billing for high-cardinality aggregations (cost-by-day-by-model) becomes expensive; need separate analytical store anyway. |
| **Azure Data Explorer (Kusto)** | Best-of-breed for compliance/observability analytics: millions of events/s, KQL `summarize`, anomaly detection, cheap cold storage tiering. Adds another service. **Not currently listed in South Africa North** — verify before assuming residency. Use only if audit volume exceeds what partitioned PG can comfortably hold (>500M rows/yr) or you already standardise on ADX for observability. |
| **TimescaleDB on Azure** | Microsoft's managed Flexible Server only ships the **Apache 2 edition** of TimescaleDB; you do *not* get compression, continuous aggregates, retention policies, or hyperfunctions on Azure-managed PG. Tiger Cloud (Tiger Data, formerly Timescale) was made GA on Azure Marketplace in late 2025 but **only in East US 2 and West Europe** — no SA North presence. Not viable for SA-residency. |

**Recommended:** keep the audit table in the *same* PostgreSQL Flexible Server, partitioned by month with an optional hash sub-partition by `tenant_id`. Use a BRIN index on `created_at` (cheap, ~3 KB) and partial b-trees for the last 7/30 days. Archive partitions older than 12 months to Azure Blob via `pg_dump`/`COPY` + Azure Storage Lifecycle. Promote to ADX *only* if rates exceed roughly 100k events/sec sustained.

---

### 5. Multi-Region (South Africa North + UK South)

| Option | Geo story |
|---|---|
| **A: PG Flexible Server** | Configure **geo-redundant backup** at *create time* to the paired region (UK West for SA North; for UK South, North Europe is paired, but you can *also* deploy a cross-region async **read replica** in any region — including SA North ↔ UK South). Replica supports promote-to-standalone. Use **virtual endpoints** so application connection strings don't change on failover. RPO ≈ <5 min for read-replica DR. Cleanest of all four options. |
| **B: Azure SQL + AI Search** | Azure SQL: active geo-replication or auto-failover groups (very mature). AI Search: no native geo-replication; you provision an index in each region and dual-feed via your indexer or Event Grid. Adds operational lift. |
| **C: PG + Cosmos DB NoSQL** | Cosmos DB has the best multi-region story in Azure (multi-write, automatic failover, 99.999% SLA) — but you'd be operating two different DR runbooks. |
| **D: Cosmos DB for PG (Citus)** | Has cross-region read replicas, but the service is retiring — do not invest in new geo topologies here. |

**For SA North + UK South specifically:** Option A wins because both regions support Flexible Server with zone-redundant HA, geo-redundant backup, and cross-region read replicas using virtual endpoints — and the entire story is one product, one IAM/Key Vault/Private Link configuration.

---

### 6. Why Option D is Eliminated

Microsoft Learn now states explicitly at the top of every Azure Cosmos DB for PostgreSQL doc page: *"Azure Cosmos DB for PostgreSQL is on a retirement path and no longer recommended for new projects. … For PostgreSQL workloads: use the Elastic Clusters feature of Azure Database For PostgreSQL."* The Elastic Clusters migration tool (April 2025) does an in-place disk-snapshot migration. For a *new* build in May 2026, you should either (a) use Flexible Server (Option A), or (b) provision Elastic Clusters from day 1 if you genuinely need horizontal scale-out. With 34 entities and a complex FK/cascade graph, Citus's distribution-column constraints will hurt; Elastic Clusters inherits the same model. Stick with single-node Flexible Server unless you measure a sharding need.

Even before retirement, Citus introduced real EF Core friction: cross-shard FKs require both tables to be distributed on the *same* column (or one to be a reference table); cascading deletes across shards are not transactional in the same way; some EF Core update strategies (e.g. ExecuteUpdate) don't always plan optimally on coordinator-routed queries.

---

### 7. Why Option B (Azure SQL + AI Search) is the Strong Runner-Up but Not the Pick

It's a solid Microsoft-native architecture and the bank's existing T-SQL skills may apply. Trade-offs that pushed it to #2 for this brief:

- **Vector search story still maturing.** Azure SQL's `VECTOR` data type went GA in June 2025 in all regions, but `CREATE VECTOR INDEX` / `VECTOR_SEARCH` (the DiskANN ANN index) is **still preview** and rolling out region-by-region. Microsoft's regional matrix as of early 2026 lists Central US, East US 2, North Central US, West US, Australia East, North Europe, UK South — **South Africa North is not yet on the GA-or-preview list for the ANN index.** Without the index, vector search is an exact KNN scan of the table.
- **EF Core 9 quirks.** EF Core 9 requires the `EFCore.SqlServer.VectorSearch` plugin (currently `9.0.0-preview.x`) for LINQ vector queries; the plugin doesn't (yet) support the new `VECTOR_SEARCH` function. EF Core 10 fixes this with built-in `SqlVector<float>` mapping and `EF.Functions.VectorDistance(...)`/`HasVectorIndex(...)` — but you specified .NET 9.
- **Two products, two cost lines, two sets of compliance evidence.** AI Search at S1 is ~$74/mo per Search Unit before semantic ranker fees. For 10k–100k vectors that's pure overhead.
- **Migration friction.** Existing PostgreSQL schema must be re-typed to T-SQL.

If you were starting greenfield with no existing PG schema, Azure SQL Hyperscale + AI Search would be very competitive — particularly for the *retrieval quality* gain from the semantic ranker. But the brief gives you an existing PG schema and asks to minimise complexity.

---

### 8. Compliance Quick Reference (all options)

Azure Database for PostgreSQL Flexible Server, Azure SQL DB, Azure AI Search, Azure Cosmos DB and Azure Data Explorer all carry the same Azure platform certifications relevant to South African banking:

- **SOC 1 / SOC 2 Type 2 / SOC 3**
- **ISO/IEC 27001:2013, 27017:2015, 27018:2014, 27701, 22301, 9001, 20000-1**
- **PCI DSS Level 1** (and Azure Policy regulatory baselines for **PCI DSS v4.0**)
- **CSA STAR Attestation + Certification**
- **HIPAA / HITECH** (de-identification controls, BAA available)
- **Reserve Bank of India IT Framework** baseline (closest published banking-regulator baseline; no equivalent SARB Azure Policy baseline is published yet, but NIST 800-53, ISO 27001, and PCI baselines map directly)

For a SARB / FSCA-regulated bank, the pertinent technical controls — **encryption at rest (AES-256, customer-managed key in Azure Key Vault Premium / Managed HSM)**, **TLS 1.2+ in transit**, **private endpoints with public access disabled**, **pgAudit / SQL Audit to Log Analytics with ≥365-day retention (CIS 5.1.2)**, **Microsoft Entra ID authentication only**, and **PITR ≥7 days with geo-redundant backup** — are available on every option. Option A is the simplest to get a single audit boundary around.

---

### 9. Final Recommended Architecture

```
┌─────────────────────────── Azure (South Africa North) ───────────────────────────┐
│                                                                                  │
│  ┌──────────── App Tier (.NET 9 / EF Core 9) ──────────┐                          │
│  │ Container Apps or AKS, behind Front Door / APIM     │                          │
│  └─────────────────────────────┬───────────────────────┘                          │
│                                │ Private Endpoint, MS Entra ID, Managed Identity │
│                                ▼                                                  │
│  ┌─────────── Azure Database for PostgreSQL Flexible Server (PG 16/17) ────────┐  │
│  │  Zone-Redundant HA, GP_Standard_D4ds_v5 (or Memory-Optimised),               │  │
│  │  CMK in Key Vault, Private Endpoint, pgAudit + Log Analytics                 │  │
│  │                                                                              │  │
│  │  Extensions: vector (0.8.0), pg_diskann, uuid-ossp, pgaudit, pg_stat_stmts, │  │
│  │              azure_ai (optional), pg_partman                                 │  │
│  │                                                                              │  │
│  │  Schema:                                                                     │  │
│  │   ├── 34 OLTP entities (UUIDv7 PKs, jsonb columns, ToJson() complex types)   │  │
│  │   ├── knowledge_chunks(embedding vector(1536)) — HNSW cosine                 │  │
│  │   ├── mission_events — append-only event store                               │  │
│  │   └── llm_call_audit — RANGE-by-month + HASH(tenant_id) partitioned;         │  │
│  │       BRIN(created_at) + partial btrees for hot windows                      │  │
│  └────────────────────────┬─────────────────────────────────────────────────────┘  │
│                           │ Cross-region read replica + virtual endpoint           │
└───────────────────────────┼──────────────────────────────────────────────────────┘
                            ▼
┌────────────────── Azure (UK South — DR / data residency twin) ──────────────────┐
│   PG Flexible Server read replica (promotable), zone-redundant HA, same CMK ref  │
└──────────────────────────────────────────────────────────────────────────────────┘
```

- Optional: layer **Azure AI Search Basic tier** on top *only* when the business asks for cross-encoder reranking or agentic retrieval. Indexer pulls changed `knowledge_chunks` rows from Postgres on a schedule.
- Optional: archive `llm_call_audit` partitions older than 12 months to **ADX in UK South** (SA North not currently a confirmed ADX region) for long-tail compliance analytics, or to Blob with Azure Synapse serverless SQL for cheap on-demand SQL.

---

## Caveats

1. **pgvector 0.8.0 + HNSW CPU-incompatibility incidents.** Microsoft Q&A reported in 2025 that on certain Flexible Server stamps the pre-built pgvector 0.8.0 binary used CPU instructions (AVX/AVX2) not present on every host VM, causing `signal 4: Illegal instruction` crashes during inserts/queries. Microsoft's recommended workaround was to deploy in another availability zone or use the `pg_diskann` extension instead. By early 2026 this is largely mitigated, but **before going to production verify with a representative load test in the exact SA North zone you'll use**, and have `pg_diskann` as a fallback indexer choice.
2. **South Africa North regional gaps.** Azure SQL DB DiskANN vector index, ADX, and Tiger Cloud TimescaleDB are not (yet) in SA North per the most recent Microsoft regional matrices. The recommendation above intentionally avoids depending on any of them. Re-verify Microsoft's "Products by region" page before contracting.
3. **EF Core 9 lifecycle.** EF Core 9 is a **Short-Term Support release** with end of support **November 10, 2026**. EF Core 10 (LTS, released November 2025) brings substantial improvements (built-in SQL Server vector support, improved JSON ExecuteUpdate, complex-type partial updates). Plan an upgrade to EF Core 10 within the first 12 months — `Pgvector.EntityFrameworkCore` 0.3.0 already targets both 9 and 10, and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x is GA, so the upgrade path is low-risk.
4. **Azure Cosmos DB for PostgreSQL retirement.** Microsoft has not published a hard end-of-life date as of May 2026, but the documentation site directs all new projects elsewhere and the Elastic Clusters migration tool was published in April 2025. Anyone still considering Option D should treat that as a strong "do not start here" signal.
5. **HorizonDB.** Microsoft announced a private preview of "Azure HorizonDB" — a Rust-based cloud-native PostgreSQL engine with DiskANN + Foundry integration — at Ignite 2025. As of May 2026 it is still private preview and not appropriate for a regulated banking workload. Worth tracking for a future re-platform.
6. **TimescaleDB community-edition features (compression, retention policies, continuous aggregates) are not available** on the Microsoft-managed Azure Database for PostgreSQL Flexible Server — only the Apache 2 edition is shipped. If those features are essential, you must self-host on a VM (out-of-scope for a managed-service brief) or use Tiger Cloud (currently East US 2 / West Europe only — fails the SA-residency test).
7. **Pricing figures cited** ($73.73 S1 Search Unit; ~$520/mo for a D4ds_v5 HA pair in SA North) are list-price approximations from the Azure pricing pages; bank-specific Enterprise Agreement, reservation, and zone-charge details will vary. Use the Azure Pricing Calculator with the live SA North rate card before committing. Reserved capacity (1y / 3y) typically yields 30–60% savings on Flexible Server compute.
8. **Compliance baselines** like "RBI - IT Framework for NBFC" are present as Azure Policy initiatives; there is no published *South African Reserve Bank* equivalent. The bank's compliance team will need to map the SOC/ISO/PCI evidence into SARB Joint Standard 1 of 2024 / Directive D6 by themselves — this is a process gap, not a technology gap.