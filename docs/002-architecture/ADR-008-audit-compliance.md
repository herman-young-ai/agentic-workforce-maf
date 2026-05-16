# ADR-008: Audit, Compliance, and Immutable Evidence

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R08-response-audit-compliance.md](../098-research/R08-response-audit-compliance.md)

---

## Context

The Agentic Workforce Platform is a dual-regulated bank platform (FCA/PRA in UK, SARB/PA in South Africa). Every LLM call, tool invocation, and human approval decision must be auditable with immutable evidence retained for 7+ years. No built-in Azure AI audit trail exists — we must build it.

## Decision

**Three-layer audit pipeline: MAF middleware → Event Hubs → Blob WORM + ADX/Eventhouse**

### Architecture

```
┌─── Agent Execution ──────────────────────────────────────────────────┐
│                                                                       │
│  IChatClient pipeline                                                │
│  ├── AuditingChatClient (DelegatingChatClient)                       │
│  │   └── Captures: model, tokens, latency, input/output hash        │
│  └── MAF FunctionMiddleware                                          │
│      └── Captures: tool name, args, result, approval decision        │
│                                                                       │
│  Backpressure via Channel<AuditRecord> (bounded, Wait + timeout)     │
└─────────────────────────┬────────────────────────────────────────────┘
                          │
                          ▼
┌─── AuditDrainService (BackgroundService) ────────────────────────────┐
│  Batches records (100 or 1s) → EventDataBatch → Event Hub            │
│  Hash chain: per-stream SHA-256 with sequence numbers                │
│  Drops logged as audit-of-audit-failures                             │
└─────────────────────────┬────────────────────────────────────────────┘
                          │
              ┌───────────┼───────────┐
              ▼                       ▼
┌── Evidence Store ──┐    ┌── Analytics Store ──────────┐
│ Azure Blob Storage │    │ Event Hubs → ADX/Eventhouse │
│ Version-level WORM │    │ KQL for compliance search   │
│ 7-year locked      │    │ 7-year retention            │
│ SHA-256 metadata   │    │ Materialized views          │
│ Per-region (SA/UK) │    │ Daily Merkle root anchor    │
└────────────────────┘    └────────────────────────────┘
```

### Layer 1: MAF Middleware (Capture)

Two middleware positions for full coverage:

```csharp
// IChatClient middleware — captures every LLM call
public sealed class AuditingChatClient(
    IChatClient inner,
    ChannelWriter<AuditRecord> sink,
    TimeProvider timeProvider,
    IOptions<AuditOptions> auditOptions)
    : DelegatingChatClient(inner)
{
    // Backpressure timeout — if the channel is full for longer than this,
    // agent execution stops. Running agents without audit is not permitted
    // in a regulated bank (Principle 8: Fail Fast).
    // Configured via AuditOptions.BackpressureTimeoutSeconds (default: 5).
    private readonly TimeSpan _writeTimeout = TimeSpan.FromSeconds(
        auditOptions.Value.BackpressureTimeoutSeconds);

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var inputHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messages));
        var response = await base.GetResponseAsync(messages, options, ct);
        sw.Stop();

        var record = new AuditRecord(
            Timestamp: timeProvider.GetUtcNow(),
            AgentName: options?.AdditionalProperties?["agentName"]?.ToString() ?? "",
            ModelId: response.ModelId ?? "",
            InputHash: Convert.ToHexString(inputHash),
            OutputHash: Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(response.Messages))),
            InputTokens: response.Usage?.InputTokenCount ?? 0,
            OutputTokens: response.Usage?.OutputTokenCount ?? 0,
            LatencyMs: sw.ElapsedMilliseconds,
            ToolCalls: ExtractToolCalls(response));

        // Backpressure: wait up to 5s for channel capacity.
        // If the drain service can't keep up (e.g. Event Hub outage + full buffer),
        // agent execution fails rather than silently dropping audit records.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_writeTimeout);
        try
        {
            await sink.WriteAsync(record, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AuditBackpressureException(
                $"Audit channel full for >{_writeTimeout.TotalSeconds}s. " +
                "Agent execution halted — cannot run without audit trail. " +
                "Check AuditDrainService health and Event Hub connectivity.");
        }

        return response;
    }
}
```

**Backpressure, not fire-and-forget:** `Channel<AuditRecord>` with `BoundedChannelFullMode.Wait` (capacity 50,000). The `AuditingChatClient` calls `WriteAsync` with a 5-second timeout. If the channel is full for >5s (drain service down, Event Hub unreachable, sustained burst beyond capacity), the write throws `AuditBackpressureException` and agent execution halts immediately. This is correct for a dual-regulated bank: running agents without an audit trail is worse than stopping agents (Principle 8: Fail Fast). The 50,000-record buffer provides ~50 seconds of headroom at 1,000 records/second, which covers transient Event Hub blips without impacting agent latency.

### Layer 2: Event Hubs (Transport)

- Event Hubs Standard, 1 TU per region (~$22/mo without Capture)
- One namespace per region (SA North, UK South) for data residency
- Partition key: `{tenantId}.{agentName}` for ordered replay per agent
- Capture enabled for free Avro archive to Blob as secondary backup

### Layer 3a: Evidence Store (Blob WORM)

Full prompt/response JSON written to immutable Blob Storage:

```csharp
// Version-level WORM with 7-year locked retention
await versioned.SetImmutabilityPolicyAsync(new BlobImmutabilityPolicy
{
    ExpiresOn = DateTimeOffset.UtcNow.Add(TimeSpan.FromDays(7 * 365 + 2)),
    PolicyMode = BlobImmutabilityPolicyMode.Locked
});
```

- **Version-level WORM** (not container-level) — per-record retention
- Storage account with `immutableStorageWithVersioning: true` at creation
- ZRS (in-region only) — no GRS to avoid cross-jurisdiction replication
- Private endpoint, no public access
- Cohasset-attested for SEC 17a-4(f), CFTC 1.31, FINRA 4511

### Layer 3b: Analytics Store (ADX/Eventhouse)

For compliance search and dashboards:

```kusto
// Find all agent actions for a project in a date range
AgentAudit
| where ingestion_time() between (datetime(2026-04-01) .. datetime(2026-04-30))
| where AgentName == "security.reviewer" and ProjectId == "PRJ-0042"
| project Timestamp, ModelId, InputTokens, OutputTokens, ToolCalls, EvidenceBlobUri

// Tamper detection: any gaps in hash chain?
AgentAudit
| summarize MinSeq = min(SeqNo), MaxSeq = max(SeqNo), Cnt = count() by StreamId
| where (MaxSeq - MinSeq + 1) != Cnt
```

- **Recommended: Microsoft Fabric Eventhouse** (consumption pricing, KQL parity) — ~$262/mo vs $700-1200/mo for dedicated ADX
- 7-year retention via `SoftDeletePeriod = 2557d`
- 30-day hot cache for recent queries

### Tamper Detection: Hash Chain

Per-stream SHA-256 with sequence numbers + daily Merkle root anchoring. Hash chain state is **persisted in Redis** so it survives pod restarts without breaking chain continuity.

```csharp
/// <summary>
/// Persists hash chain state (seq + prevHash) per stream in Redis.
/// On pod restart, the chain resumes from the last persisted state.
/// If Redis is unavailable, SignAsync() throws — agent execution halts because
/// unchained audit records are inadmissible (Principle 8: Fail Fast).
///
/// Uses a per-stream Redis lock to serialize the read-compute-write cycle.
/// SHA-256 cannot be computed inside Redis Lua, so the hash computation
/// happens in C# between the read and write. Without the lock, concurrent
/// writers to the same stream would read stale `prev` values, corrupting
/// the chain. The lock ensures only one writer operates on a stream at a time.
/// </summary>
public sealed class HashChainWriter(
    IConnectionMultiplexer redis,
    IOptions<AuditOptions> auditOptions)
{
    private const string ChainKeyPrefix = "audit:chain:";
    private const string LockKeyPrefix = "audit:chain:lock:";

    // Configured via AuditOptions.HashChainLockTimeoutSeconds (default: 5).
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(
        auditOptions.Value.HashChainLockTimeoutSeconds);

    // Atomic Lua: increment seq, read prev. Runs under lock so no concurrent reader.
    private static readonly LuaScript ReadAndIncrementScript = LuaScript.Prepare(
        """
        local key = @key
        local seq = redis.call('HINCRBY', key, 'seq', 1)
        local prev = redis.call('HGET', key, 'prev') or string.rep('0', 64)
        return {seq, prev}
        """);

    public async Task<AuditRecord> SignAsync(string streamId, AuditRecord skeleton)
    {
        var db = redis.GetDatabase();
        var chainKey = ChainKeyPrefix + streamId;
        var lockKey = LockKeyPrefix + streamId;

        // Per-stream lock: serializes the read → compute → write cycle.
        // Without this, concurrent writers to the same stream read stale
        // prev values, producing a corrupted chain.
        var lockToken = Guid.NewGuid().ToString();
        if (!await db.LockTakeAsync(lockKey, lockToken, _lockTimeout))
            throw new AuditLockTimeoutException(
                $"Could not acquire hash chain lock for stream '{streamId}' " +
                $"within {_lockTimeout.TotalSeconds}s. " +
                "Another writer may be stuck. Check AuditDrainService health.");

        try
        {
            // Phase 1: Atomically increment seq and read previous hash (under lock)
            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                ReadAndIncrementScript, new { key = (RedisKey)chainKey });

            if (result is null || result.Length != 2)
                throw new InvalidOperationException(
                    $"Hash chain Redis script returned unexpected result for stream '{streamId}'.");

            var seq = (long)result[0];
            var prev = (string?)result[1] ?? new string('0', 64);

            // Phase 2: Compute record hash (in C# — SHA-256 not available in Redis Lua)
            var withChain = skeleton with { SeqNo = seq, PrevHash = prev };
            var canonical = JsonSerializer.SerializeToUtf8Bytes(withChain with { RecordHash = "" });
            var recordHash = Convert.ToHexString(SHA256.HashData(canonical));

            // Phase 3: Persist new prev hash (under lock — no concurrent reader sees stale state)
            await db.HashSetAsync(chainKey, "prev", recordHash);

            return withChain with { RecordHash = recordHash };
        }
        finally
        {
            await db.LockReleaseAsync(lockKey, lockToken);
        }
    }
}
```

**Why Redis, not PostgreSQL?**
- Redis is already in the architecture (queue, pub/sub, SignalR backplane, session cache)
- Atomic operations via Lua scripts — no transaction overhead
- Sub-millisecond latency — doesn't bottleneck the audit pipeline
- Redis persistence (AOF) ensures state survives Redis restarts
- If Redis is unavailable, `SignAsync` throws and agent execution halts — correct behaviour

**Why not in-memory?**
- Pod restarts (deployments, scaling, crashes) lose in-memory state
- After restart, the chain would fork from genesis (`seq=1, prev=000...0`), creating a detectable gap
- Regulators would flag the gap as a potential tampering window

**Continuity guarantees:**
- Stream = `{region}.{agentName}.{instanceId}`
- On first use of a new stream, Redis returns `seq=1` and `prev=000...0` (genesis) — correct for a new stream
- On pod restart, Redis returns the last persisted `seq` and `prev` — chain continues seamlessly
- Daily Merkle root written to separate WORM blob for period-level verification
- Verification: linear pass checking sequence continuity + hash chain integrity via KQL

## Data Residency

| Region | Storage Account | Event Hub Namespace | ADX/Eventhouse |
|--------|----------------|--------------------|-|
| SA North | `auditevdsano01` (ZRS) | `audit-eh-sano` | Shared cluster or Eventhouse |
| UK South | `auditevdukso01` (ZRS) | `audit-eh-ukso` | Shared cluster or Eventhouse |

No cross-region replication. Geo-DR disabled. Each region is a complete, independent audit boundary.

## Cost (per region, monthly steady-state)

| Component | Config | Cost |
|-----------|--------|------|
| Event Hubs Standard | 1 TU, no Capture | ~$22 |
| Blob Storage (evidence) | ~15 GB/mo, Cool after 30d, ZRS | ~$15 |
| Fabric Eventhouse | F2 capacity | ~$262 |
| **Total per region** | | **~$300/mo** |
| **Both regions** | | **~$600/mo** |

Per-record cost: ~$0.00083/audited turn (~$0.83 per 1,000 turns).

## Regulatory Mapping

| Requirement | Azure Mechanism |
|-------------|----------------|
| SS1/23 model decision evidence | WORM Blob + hash chain + ADX queryable store |
| FG16/5 cloud outsourcing controls | Azure FCA/PRA compliance offering + SOC/ISO attestations |
| POPIA data residency | SA North ZRS storage, no GRS |
| 7-year retention | Blob WORM locked retention + ADX SoftDeletePeriod |
| Tamper evidence | SHA-256 hash chain + daily Merkle root + immutable Blob |
| Audit of audit failures | Separate WORM append blob for dropped records |

## Consequences

- No built-in Azure AI audit trail — this subsystem is custom-built
- Blob Storage account must have `immutableStorageWithVersioning` enabled at creation (cannot retrofit)
- Writing principal needs `Storage Blob Data Owner` role (includes WORM super-user action)
- ADX cluster cost dominates at low volume — Fabric Eventhouse is the pragmatic answer
- Hash chain is per-stream (per agent instance) — state persisted in Redis, not in-memory
- Hash chain requires Redis availability — if Redis is down, audit signing fails and agent execution halts (fail fast)
- `AuditBackpressureException` halts agent execution when audit channel is saturated — operators must monitor `AuditDrainService` health
- SS1/23 has no Azure-native compliance template — bank must build custom Purview Compliance Manager assessment
- Event Hubs Standard retention is 7 days max — long-term retention is via ADX + Blob, not Event Hubs itself

### Principle Compliance

- **P14 Secure by Default:** Access to evidence stores (Blob WORM, Eventhouse) defaults to deny-all. Explicit allowlist of identities that can query or export audit data; empty allowlist = no access.
- **P15 Backend Owns All Logic:** Compliance queries, tamper detection, and hash chain verification run exclusively server-side. Dashboards are display-only — cannot construct custom evidence queries client-side.
- **P16 Single Source of Truth:** WORM Blob is the single authoritative source for audit evidence. Eventhouse is a read replica for analytics, not an authoritative store. If they diverge, WORM wins.
- **P18 Idempotency:** Audit record writes are idempotent — the hash chain's `{StreamId}.{SeqNo}` serves as a natural idempotency key. Duplicate Event Hub deliveries produce the same WORM blob, not duplicates.
- **P8 Fail Fast:** Audit channel uses `Wait` mode with 5s timeout — agent execution halts if audit pipeline is saturated. Hash chain `SignAsync` throws if Redis is unreachable. No silent degradation of audit capability.
- **P19 Bounded Resource Usage:** AuditDrainService has explicit bounds: max batch size (100), max drain interval (1s), max channel capacity (50,000), and backpressure via `WriteAsync` timeout when Event Hub is unreachable beyond 5 seconds.
- **P20 Version Everything:** `AuditRecord` schema includes a `schemaVersion` field so future format changes maintain backward compatibility with the 7-year WORM archive.
- **P21 Explicit Over Implicit:** The list of audited events is declared explicitly in configuration — which middleware, which tool calls, which agents. No auto-discovery of pipeline positions.
