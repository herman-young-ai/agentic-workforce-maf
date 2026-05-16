# R04: Data Layer — EF Core + PostgreSQL + pgvector vs Cosmos DB + Azure AI Search

## Prompt for claude.ai

---

I am designing the data layer for an AI agent orchestration platform in C# / .NET 9. The system has 34 domain entities (relational), vector search for a knowledge platform, and high-volume audit logging for LLM calls. This runs on Azure for a regulated bank.

I need a **concise architectural comparison** of data layer options. Tables and clear recommendations.

### Our Data Requirements

**Relational (ACID, strong consistency):**
- 34 entities with complex FK relationships (missions → sessions → messages, missions → executions → tasks → attempts, etc.)
- UUID primary keys (UUIDv7 for time-ordering)
- JSON columns for flexible schemas (task_plan_json, model_config_json, context_data)
- Pagination, filtering, sorting on most entities
- Cascading deletes (mission deletion cascades through ~15 related tables)
- Optimistic concurrency on versioned entities (MCD, prompts)

**Vector search (knowledge platform):**
- 1536-dimension embeddings (OpenAI text-embedding-3-small)
- Cosine similarity search over learnings, decisions, milestones
- Hybrid search: structured filter (tags, mission_id, kind) + vector rerank
- ~10k-100k embeddings per deployment (not millions)
- Embeddings stored with source_table, source_id, mission_id, version, content_hash

**High-volume write (LLM call audit):**
- Every LLM call recorded: agent_name, model, provider, input_tokens, output_tokens, cache_tokens, cost_usd, latency_ms
- Aggregation queries: cost per agent, per mission, per hour; token economics; cache hit rates
- Retention: potentially millions of rows over months
- Must be queryable for compliance audit

**Event store (real-time):**
- Mission events (state transitions, agent actions, console output)
- Published via Redis pub/sub for SSE/SignalR
- Persisted to DB for replay/audit
- High write throughput, append-only

### Options to Compare

**Option A: PostgreSQL Flexible Server + pgvector (single database)**
- EF Core + Npgsql for relational
- pgvector extension for embeddings
- Same DB for everything

**Option B: Azure SQL + Azure AI Search (split)**
- Azure SQL for relational entities
- Azure AI Search for vector/semantic search
- Separate indexing pipeline

**Option C: PostgreSQL + Azure Cosmos DB (split by workload)**
- PostgreSQL for relational (missions, sessions, catalog)
- Cosmos DB for high-volume/schemaless (run instances, LLM calls, events)
- pgvector or Azure AI Search for vectors

**Option D: Azure Cosmos DB PostgreSQL (Citus) + pgvector**
- Distributed PostgreSQL with pgvector
- Single API, horizontally scalable

### Comparison Table

For each option, assess:

| Criterion | Option A | Option B | Option C | Option D |
|---|---|---|---|---|
| EF Core support maturity | | | | |
| JSON column support | | | | |
| Vector search performance (10k-100k vectors) | | | | |
| Hybrid search (structured + vector) | | | | |
| Cascading FK deletes | | | | |
| High-volume write throughput (LLM calls) | | | | |
| Aggregation query performance | | | | |
| Azure managed service availability | | | | |
| Data residency (South Africa North region) | | | | |
| Operational complexity | | | | |
| Cost at our scale | | | | |
| Migration story from existing PostgreSQL schema | | | | |
| Compliance (encryption at rest, audit logging, PITR) | | | | |

### Specific Technical Questions

1. **EF Core + Npgsql + pgvector**: What NuGet packages are needed? Can I map a `Vector` column type in EF Core and do similarity queries via LINQ? Show a 5-line code example.

2. **Azure AI Search vs pgvector**: For our scale (10k-100k embeddings, hybrid search), is Azure AI Search overkill? What does it give us that pgvector doesn't?

3. **LLM call table**: With millions of rows, should this live in the main relational DB or a separate store (Cosmos DB, Azure Data Explorer, TimescaleDB)?

4. **Multi-region**: If we need to deploy to South Africa North and UK South for data residency, which option handles geo-replication most cleanly?

### Output Format

- Comparison table filled in
- Answer each of the 4 specific questions
- **Recommendation**: which option, with rationale for a bank
- **Schema sketch**: 10-line EF Core DbContext showing how the chosen option maps Mission, Session, Embedding, and LlmCall entities

Keep total response under 2000 words. Tables and code preferred.

---

## After Research

Save claude.ai's response as: `docs/098-research/R04-response-data-layer.md`
