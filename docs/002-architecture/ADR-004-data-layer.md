# ADR-004: Data Layer Architecture

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R04-response-data-layer.md](../098-research/R04-response-data-layer.md)

---

## Context

The Agentic Workforce Platform has 34 domain entities with complex FK relationships, vector search for a knowledge platform (10k-100k embeddings), high-volume LLM call audit logging, and real-time event persistence. This runs on Azure in South Africa North for a regulated bank.

## Decision

**Option A: PostgreSQL Flexible Server + pgvector (single database)**

One PostgreSQL Flexible Server in South Africa North hosts everything:
- 34 OLTP entities with EF Core 9 + Npgsql
- Vector search via pgvector 0.8.0 with HNSW indexes
- LLM call audit via time-partitioned table (RANGE by month)
- Event store as append-only table

### Why PostgreSQL Wins

| Factor | Assessment |
|--------|-----------|
| EF Core 9 maturity | Npgsql.EFCore.PostgreSQL 9.0.x is GA, fully tracks EF Core 9 |
| JSON columns | Native `jsonb` with `ToJson()`, LINQ → `->>` operators, GIN indexing |
| Vector search (10k-100k) | pgvector 0.8.0 HNSW + iterative scan handles 100k×1536 with sub-50ms p95 |
| Hybrid search | pgvector iterative scan + `tsvector` BM25; no built-in semantic reranker (acceptable at our scale) |
| Cascading FK deletes | Native `ON DELETE CASCADE` works perfectly for 34 entities |
| UUIDv7 | Npgsql 9.0 generates UUIDv7 client-side by default |
| Optimistic concurrency | `IsRowVersion()` on `xmin` system column — zero-effort |
| SA North availability | GA with 3 AZs, zone-redundant HA, geo-redundant backup |
| Migration from existing PG schema | Trivial — pg_dump/pg_restore |
| Compliance | SOC 1/2/3, ISO 27001, PCI DSS L1; pgAudit extension; CMK via Key Vault |
| Operational complexity | **One database, one connection string, one backup story** |
| Cost | ~$520/mo for GP_Standard_D4ds_v5 with HA (before reserved capacity discounts) |

### LLM Call Audit Strategy

Keep in the same PostgreSQL server, but as a **partitioned table**:

```sql
CREATE TABLE llm_calls (
    id UUID DEFAULT gen_random_uuid(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    agent_name VARCHAR(200) NOT NULL,
    model VARCHAR(100) NOT NULL,
    ...
) PARTITION BY RANGE (created_at);

-- Monthly partitions, auto-created by pg_partman
-- BRIN index on created_at (3 KB, cheap writes)
-- Partition pruning keeps queries fast
-- DROP PARTITION for instant retention cleanup
-- Archive to Azure Blob after 12 months
```

Scales to hundreds of millions of rows. Only consider Azure Data Explorer if sustained write rate exceeds ~100k events/sec.

### Vector Search Strategy

pgvector 0.8.0 with HNSW cosine similarity:

```csharp
// EF Core LINQ → pgvector <=> operator
var results = await db.KnowledgeChunks
    .Where(c => c.ProjectId == projectId)
    .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
    .Take(10)
    .ToListAsync();
```

Only add Azure AI Search if we need cross-encoder semantic reranking or scale past ~1M chunks.

### Multi-Region (SA North + UK South)

Cross-region read replica with virtual endpoints:
- SA North: primary (read-write)
- UK South: async read replica (promotable for DR)
- RPO ≈ <5 min, single product, one IAM/Key Vault/Private Link config

## Alternatives Considered

| Option | Verdict | Why Not |
|--------|---------|---------|
| Azure SQL + Azure AI Search | Runner-up | Two products; DiskANN vector index still preview (not confirmed in SA North); migration friction from PG to T-SQL; $74/mo AI Search overhead unnecessary at our scale |
| PostgreSQL + Cosmos DB split | Rejected | Over-engineered for 10k-100k embeddings; breaks transactional integrity; two heterogeneous DR runbooks |
| Cosmos DB for PostgreSQL (Citus) | Eliminated | On Microsoft retirement path; cross-shard FK cascade limitations incompatible with 34-entity model |

## Key Packages

```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.4" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
```

PostgreSQL extensions to enable:
- `vector` (pgvector 0.8.0)
- `pg_diskann` (fallback vector index)
- `uuid-ossp`
- `pgaudit` (compliance audit logging)
- `pg_stat_statements`
- `pg_partman` (automatic partition management)

## Consequences

- Single database simplifies operations but means one failure domain — mitigated by zone-redundant HA
- pgvector has no built-in semantic reranker — acceptable at our scale; add AI Search later if needed
- EF Core 9 is short-term support (EOL Nov 2026) — plan upgrade to EF Core 10 within 12 months; `Pgvector.EntityFrameworkCore` 0.3.0 already supports both
- pgvector 0.8.0 had CPU-incompatibility issues on some Azure stamps — load test in target SA North zone before production; `pg_diskann` available as fallback
- Archive audit partitions >12 months to Azure Blob for 7-year compliance retention

### Principle Compliance

- **P15 Backend Owns All Logic:** All data validation, constraint enforcement, cascading operations, and computed fields are in the EF Core domain layer and PostgreSQL constraints — never in client-side logic. Embedding generation, knowledge deduplication, and partition management are server-side.
- **P17 Human Authority:** Knowledge records can be retracted by humans. Agent-generated data (PCD updates, learnings) can be reverted by a human at any time. Audit partitions can only be archived or dropped by Platform Admin — agents cannot trigger data retention operations.
- **P18 Idempotency:** All writes use optimistic concurrency via `xmin` version checks. Create operations check for existing records before inserting. The append-only event store deduplicates events by `EventId`. LLM call audit inserts handle replay-driven duplicates keyed by execution ID + sequence number.
- **P20 Version Everything:** Schema changes managed exclusively through EF Core migrations with rollback scripts. Every migration is numbered and reversible. `format_version` on entities tracks document format evolution. Old formats migrated transparently on read — never rejected.
- **P21 Explicit Over Implicit:** All EF Core entity configurations use explicit `HasColumnName`, `HasColumnType`, and `IsRequired` declarations — no convention-based mapping. JSON columns use explicit `ToJson()` or `[Column(TypeName = "jsonb")]`. All indexes declared explicitly, not auto-generated.

## Azure Resource

```
Azure Database for PostgreSQL Flexible Server
├── SKU: GP_Standard_D4ds_v5 (or Memory-Optimised for vector workload)
├── HA: Zone-redundant (3 AZs in SA North)
├── Backup: Geo-redundant, 35-day PITR
├── Auth: Entra ID + Managed Identity (no password auth)
├── Network: Private endpoint, public access disabled
├── Encryption: CMK in Azure Key Vault Premium
├── Monitoring: pgAudit → Log Analytics (≥365-day retention)
└── Read Replica: UK South (cross-region DR)
```
