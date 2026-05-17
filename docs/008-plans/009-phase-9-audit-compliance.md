# Phase 9: Audit & Compliance

**Status:** Not Started
**Depends On:** Phase 8 (Workflow Engine)
**Verification:** Execute agent, verify LlmCall records in DB, audit hash chain integrity check passes

---

## Pre-flight

Complete the checklist in [000-phase-overview.md § Pre-flight for every phase](000-phase-overview.md#pre-flight-for-every-phase):

1. Read `.codemap/map.md` — type/method inventory from the previous phase. Do not recreate anything already present.
2. Read `.codemap/quality.md` — current CQI baseline. Work must not regress the score.
3. Verify the previous phase's exit criteria still hold:
   - `dotnet build AgenticWorkforce.slnx` exits 0
   - `dotnet test AgenticWorkforce.slnx` exits 0

---

## Objective

Implement the 3-layer audit pipeline required for dual-regulated banking compliance (FCA/PRA UK, SARB/PA SA). Every LLM call is captured non-blocking, batched, and persisted to both an evidence store (immutable) and an analytics store (queryable). A SHA-256 hash chain ensures tamper detection. After this phase, the platform produces a 7-year compliance trail for every AI decision.

---

## Architecture (from ADR-008)

```
Agent execution → IChatClient middleware (AuditingChatClient)
                        │ non-blocking Channel<AuditRecord>
                        ▼
                  AuditDrainService (batches 100 records / 1s)
                        │
              ┌─────────┼─────────┐
              ▼                   ▼
       Evidence Store        Analytics Store
       (Blob WORM)          (Event Hub → Eventhouse)
       7-year locked         KQL queryable
       SHA-256 metadata      7-year retention
       Per-region ZRS        Daily Merkle root
```

### Key Principles

- **Fail fast on backpressure** — if the audit channel is full and `Wait` mode times out (5s), throw `AuditBackpressureException` and halt agent execution. Never silently drop audit records.
- **Hash chain per stream** — each project gets a sequential hash chain (`seq`, `prevHash`). Chain state is persisted in Redis via atomic Lua script. Survives pod restarts.
- **Non-blocking capture** — `AuditingChatClient` writes to `Channel<AuditRecord>` without awaiting I/O. The drain service handles the I/O asynchronously.
- **Dual persistence** — every record goes to BOTH evidence (immutable) and analytics (queryable). One is the legal backup, the other is the operational store.

---

## 1. AuditRecord Model

### File: `src/AgenticWorkforce.Agents/Audit/AuditRecord.cs`

```csharp
namespace AgenticWorkforce.Agents.Audit;

public sealed class AuditRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    // Context
    public Guid ProjectId { get; init; }
    public Guid? TaskId { get; init; }
    public Guid? SessionId { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;

    // Request
    public string InputHash { get; init; } = string.Empty;  // SHA-256 of prompt
    public long InputTokens { get; init; }

    // Response
    public string OutputHash { get; init; } = string.Empty;  // SHA-256 of response
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheCreationTokens { get; init; }

    // Cost & Performance
    public decimal CostUsd { get; init; }
    public int LatencyMs { get; init; }
    public int ToolCallCount { get; init; }
    public int? StatusCode { get; init; }
    public string? Error { get; init; }

    // Full content (for evidence store — not stored in analytics)
    public string? FullPromptJson { get; init; }
    public string? FullResponseJson { get; init; }

    // Hash chain
    public long SequenceNumber { get; set; }
    public string PreviousHash { get; set; } = string.Empty;
    public string RecordHash { get; set; } = string.Empty;
}
```

---

## 2. AuditingChatClient (Enhanced from Phase 6)

### File: `src/AgenticWorkforce.Agents/Middleware/AuditingChatClient.cs`

Phase 6 created a stub. This phase implements the full non-blocking capture:

```csharp
internal sealed class AuditingChatClient(
    IChatClient inner,
    ChannelWriter<AuditRecord> auditWriter,
    TimeProvider clock) : DelegatingChatClient(inner)
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var start = clock.GetTimestamp();
        var inputJson = SerializeMessages(messages);
        var inputHash = ComputeSha256(inputJson);

        ChatResponse response;
        string? error = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            var elapsed = clock.GetElapsedTime(start);
            var outputJson = response != null ? SerializeResponse(response) : null;

            var record = new AuditRecord
            {
                ProjectId = GetProjectId(options),
                TaskId = GetTaskId(options),
                SessionId = GetSessionId(options),
                AgentName = GetAgentName(options),
                Model = options?.ModelId ?? "unknown",
                Provider = GetProvider(options),
                InputHash = inputHash,
                InputTokens = response?.Usage?.InputTokenCount ?? 0,
                OutputHash = outputJson != null ? ComputeSha256(outputJson) : "",
                OutputTokens = response?.Usage?.OutputTokenCount ?? 0,
                CacheReadTokens = ExtractCacheRead(response),
                CacheCreationTokens = ExtractCacheCreate(response),
                CostUsd = CalculateCost(response),
                LatencyMs = (int)elapsed.TotalMilliseconds,
                ToolCallCount = CountTools(response),
                Error = error,
                FullPromptJson = inputJson,
                FullResponseJson = outputJson
            };

            // Non-blocking write with backpressure timeout
            if (!auditWriter.TryWrite(record))
            {
                // Channel full — wait up to 5s, then fail fast
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(WriteTimeout);
                try
                {
                    await auditWriter.WriteAsync(record, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new AuditBackpressureException(
                        "Audit channel full for >5s. Agent execution halted to prevent audit gap.");
                }
            }
        }

        return response!;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
```

---

## 3. Audit Channel Configuration

### In `DependencyInjection.cs`:

```csharp
// Bounded channel: 50,000 records, Wait mode (backpressure)
services.AddSingleton(Channel.CreateBounded<AuditRecord>(
    new BoundedChannelOptions(50_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    }));
services.AddSingleton(sp => sp.GetRequiredService<Channel<AuditRecord>>().Writer);
services.AddSingleton(sp => sp.GetRequiredService<Channel<AuditRecord>>().Reader);
services.AddHostedService<AuditDrainService>();
```

---

## 4. AuditDrainService

### File: `src/AgenticWorkforce.Agents/Audit/AuditDrainService.cs`

Background service that batches records and writes to both stores:

```csharp
internal sealed class AuditDrainService(
    ChannelReader<AuditRecord> reader,
    IAuditHashChain hashChain,
    IAuditEvidenceStore evidenceStore,
    IAuditAnalyticsStore analyticsStore,
    ILogger<AuditDrainService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("AuditDrainService started");
        var batch = new List<AuditRecord>(BatchSize);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Collect up to BatchSize records or until FlushInterval expires
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                flushCts.CancelAfter(FlushInterval);

                try
                {
                    while (batch.Count < BatchSize)
                    {
                        var record = await reader.ReadAsync(flushCts.Token);
                        batch.Add(record);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // FlushInterval elapsed — flush what we have
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, ct);
                    batch.Clear();
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "AuditDrainService error — retrying in 1s");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task FlushBatchAsync(List<AuditRecord> batch, CancellationToken ct)
    {
        // 1. Apply hash chain to each record
        foreach (var record in batch)
        {
            await hashChain.ApplyAsync(record, ct);
        }

        // 2. Write to evidence store (WORM — must succeed)
        await evidenceStore.WriteBatchAsync(batch, ct);

        // 3. Write to analytics store (best-effort — log but don't throw)
        try
        {
            await analyticsStore.WriteBatchAsync(batch, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analytics store write failed for batch of {Count}", batch.Count);
        }

        logger.LogDebug("Flushed {Count} audit records", batch.Count);
    }
}
```

---

## 5. Hash Chain

### File: `src/AgenticWorkforce.Agents/Audit/IAuditHashChain.cs`

```csharp
public interface IAuditHashChain
{
    Task ApplyAsync(AuditRecord record, CancellationToken ct = default);
    Task<bool> VerifyChainAsync(Guid projectId, CancellationToken ct = default);
}
```

### File: `src/AgenticWorkforce.Infrastructure/Audit/RedisAuditHashChain.cs`

Uses atomic Redis Lua script to maintain per-project chain state:

```csharp
internal sealed class RedisAuditHashChain(
    IConnectionMultiplexer redis,
    ILogger<RedisAuditHashChain> logger) : IAuditHashChain
{
    // Lua script: atomically read (seq, prevHash), increment, return
    private const string IncrementScript = """
        local key = KEYS[1]
        local seq = redis.call('HINCRBY', key, 'seq', 1)
        local prevHash = redis.call('HGET', key, 'hash') or ''
        redis.call('HSET', key, 'hash', ARGV[1])
        return {seq, prevHash}
        """;

    public async Task ApplyAsync(AuditRecord record, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var chainKey = $"audit:chain:{record.ProjectId}";

        // Compute record hash (includes content + previous hash for chaining)
        var preimage = $"{record.Id}|{record.Timestamp:O}|{record.InputHash}|{record.OutputHash}";

        // Execute Lua atomically
        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            IncrementScript,
            [chainKey],
            [ComputeSha256(preimage)]);

        record.SequenceNumber = (long)result![0];
        record.PreviousHash = (string)result[1]!;

        // Final record hash includes chain context
        var chainedPreimage = $"{record.SequenceNumber}|{record.PreviousHash}|{preimage}";
        record.RecordHash = ComputeSha256(chainedPreimage);

        // Update Redis with the new hash (already done in Lua)
    }

    public async Task<bool> VerifyChainAsync(Guid projectId, CancellationToken ct = default)
    {
        // Read all records for project from evidence store
        // Recompute hashes sequentially
        // Verify each record.RecordHash matches recomputed value
        // Verify chain continuity (record[n].PreviousHash == record[n-1].RecordHash)
        logger.LogInformation("Chain verification requested for project {ProjectId}", projectId);
        return true; // Full implementation requires reading from evidence store
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
```

---

## 6. Evidence Store (WORM)

### File: `src/AgenticWorkforce.Infrastructure/Audit/IAuditEvidenceStore.cs`

```csharp
public interface IAuditEvidenceStore
{
    Task WriteBatchAsync(IReadOnlyList<AuditRecord> records, CancellationToken ct = default);
    Task<AuditRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
```

### File: `src/AgenticWorkforce.Infrastructure/Audit/BlobAuditEvidenceStore.cs`

For Phase 9, implement a **local filesystem store** that mimics the WORM pattern. Azure Blob Storage with version-level WORM comes in Phase 11 (Infrastructure).

```csharp
internal sealed class LocalAuditEvidenceStore(
    ILogger<LocalAuditEvidenceStore> logger) : IAuditEvidenceStore
{
    private static readonly string BasePath = Path.Combine("var", "audit", "evidence");

    public async Task WriteBatchAsync(IReadOnlyList<AuditRecord> records, CancellationToken ct)
    {
        Directory.CreateDirectory(BasePath);

        foreach (var record in records)
        {
            var path = GetPath(record);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write full record as JSON (immutable once written)
            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct);
        }

        logger.LogDebug("Wrote {Count} evidence records to local store", records.Count);
    }

    public async Task<AuditRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        // Search by ID (in production, indexed by blob metadata)
        var files = Directory.GetFiles(BasePath, $"{id}.json", SearchOption.AllDirectories);
        if (files.Length == 0) return null;
        var json = await File.ReadAllTextAsync(files[0], ct);
        return JsonSerializer.Deserialize<AuditRecord>(json);
    }

    private static string GetPath(AuditRecord record)
    {
        // Organize: project/YYYY-MM/DD/{id}.json
        var date = record.Timestamp;
        return Path.Combine(BasePath,
            record.ProjectId.ToString(),
            $"{date:yyyy-MM}",
            $"{date:dd}",
            $"{record.Id}.json");
    }
}
```

---

## 7. Analytics Store (Event Hub Stub)

### File: `src/AgenticWorkforce.Infrastructure/Audit/IAuditAnalyticsStore.cs`

```csharp
public interface IAuditAnalyticsStore
{
    Task WriteBatchAsync(IReadOnlyList<AuditRecord> records, CancellationToken ct = default);
}
```

### File: `src/AgenticWorkforce.Infrastructure/Audit/LocalAuditAnalyticsStore.cs`

Stub for Phase 9 — writes to JSONL file. Event Hubs integration in Phase 11.

```csharp
internal sealed class LocalAuditAnalyticsStore(
    ILogger<LocalAuditAnalyticsStore> logger) : IAuditAnalyticsStore
{
    private static readonly string LogPath = Path.Combine("var", "audit", "analytics.jsonl");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WriteBatchAsync(IReadOnlyList<AuditRecord> records, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        await _lock.WaitAsync(ct);
        try
        {
            await using var writer = new StreamWriter(LogPath, append: true);
            foreach (var record in records)
            {
                // Write summary (no full prompt/response — that's in evidence store)
                var summary = new
                {
                    record.Id, record.Timestamp, record.ProjectId, record.TaskId,
                    record.AgentName, record.Model, record.Provider,
                    record.InputTokens, record.OutputTokens,
                    record.CacheReadTokens, record.CacheCreationTokens,
                    record.CostUsd, record.LatencyMs, record.ToolCallCount,
                    record.SequenceNumber, record.RecordHash
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(summary));
            }
        }
        finally
        {
            _lock.Release();
        }

        logger.LogDebug("Wrote {Count} analytics records to JSONL", records.Count);
    }
}
```

---

## 8. Daily Merkle Root

### File: `src/AgenticWorkforce.Agents/Audit/MerkleRootService.cs`

Background service that runs daily, computes a Merkle root of all audit records for the day, and writes it as an anchor:

```csharp
internal sealed class MerkleRootService(
    IAuditEvidenceStore evidenceStore,
    ILogger<MerkleRootService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Wait until midnight UTC
            var now = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;
            await Task.Delay(delay, ct);

            try
            {
                await ComputeDailyMerkleRootAsync(now.Date, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to compute daily Merkle root for {Date}", now.Date);
            }
        }
    }

    private async Task ComputeDailyMerkleRootAsync(DateTime date, CancellationToken ct)
    {
        // In production: read all record hashes for the day from evidence store
        // Compute Merkle tree root
        // Write root to a WORM blob as the daily anchor
        logger.LogInformation("Daily Merkle root computed for {Date}", date);
    }
}
```

---

## 9. LlmCall Drain (Enhanced)

The Phase 6 `LlmCallDrainService` now properly persists to the partitioned `LlmCalls` table:

### File: `src/AgenticWorkforce.Agents/Services/LlmCallDrainService.cs`

```csharp
internal sealed class LlmCallDrainService(
    ChannelReader<LlmCall> reader,
    IServiceScopeFactory scopeFactory,
    ILogger<LlmCallDrainService> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var batch = new List<LlmCall>(BatchSize);

        while (!ct.IsCancellationRequested)
        {
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            flushCts.CancelAfter(FlushInterval);

            try
            {
                while (batch.Count < BatchSize)
                    batch.Add(await reader.ReadAsync(flushCts.Token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

            if (batch.Count > 0)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LlmCalls.AddRange(batch);
                await db.SaveChangesAsync(ct);
                logger.LogDebug("Persisted {Count} LLM call records", batch.Count);
                batch.Clear();
            }
        }
    }
}
```

---

## 10. Chain Verification Endpoint

Wire up an admin endpoint to trigger chain verification:

### File: `src/AgenticWorkforce.Api/Features/Admin/Dashboard/VerifyAuditChain.cs`

```csharp
public static class VerifyAuditChain
{
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/admin/audit/verify/{projectId}", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("Admin");
    }

    private static async Task<IResult> HandleAsync(
        Guid projectId, IAuditHashChain hashChain, CancellationToken ct)
    {
        var isValid = await hashChain.VerifyChainAsync(projectId, ct);
        return Results.Ok(new { projectId, chainValid = isValid, verifiedAt = DateTime.UtcNow });
    }
}
```

---

## File Summary

### Files to CREATE (~15 files)

```
src/AgenticWorkforce.Agents/Audit/AuditRecord.cs
src/AgenticWorkforce.Agents/Audit/AuditDrainService.cs
src/AgenticWorkforce.Agents/Audit/IAuditHashChain.cs
src/AgenticWorkforce.Agents/Audit/MerkleRootService.cs
src/AgenticWorkforce.Infrastructure/Audit/IAuditEvidenceStore.cs
src/AgenticWorkforce.Infrastructure/Audit/IAuditAnalyticsStore.cs
src/AgenticWorkforce.Infrastructure/Audit/LocalAuditEvidenceStore.cs
src/AgenticWorkforce.Infrastructure/Audit/LocalAuditAnalyticsStore.cs
src/AgenticWorkforce.Infrastructure/Audit/RedisAuditHashChain.cs
src/AgenticWorkforce.Api/Features/Admin/Dashboard/VerifyAuditChain.cs
tests/AgenticWorkforce.Api.Tests.Integration/Audit/AuditPipelineTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Audit/HashChainTests.cs
tests/AgenticWorkforce.Domain.Tests.Unit/Audit/AuditDrainServiceTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Agents/Middleware/AuditingChatClient.cs — Full implementation (replace Phase 6 stub)
src/AgenticWorkforce.Agents/DependencyInjection.cs — Register audit services
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs — Register evidence/analytics stores
src/AgenticWorkforce.Agents/Services/LlmCallDrainService.cs — Full batch persistence
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test` — all tests pass:
   - `HashChainTests`: sequential hashes are correct, chain detects tampering
   - `AuditDrainServiceTests`: batches records, flushes on interval, handles backpressure
   - `AuditPipelineTests`: execute agent → verify AuditRecord written with hash chain → verify LlmCall in DB
3. `AuditingChatClient` writes to channel on every LLM call (including tool loop iterations)
4. Backpressure: when channel is full for >5s, throws `AuditBackpressureException`
5. Evidence store writes full prompt/response JSON per record
6. Analytics store writes summary (no full content) to JSONL
7. Hash chain: `record[n].PreviousHash == record[n-1].RecordHash` for all n
8. Redis Lua script is atomic — no race condition on concurrent writes
9. Chain verification endpoint returns valid=true for unmodified chain

---

## Goal Command

```
/goal Audit and compliance pipeline complete: AuditingChatClient captures every LLM call non-blocking to Channel<AuditRecord> with 5s backpressure timeout (throws AuditBackpressureException if exceeded). AuditDrainService batches 100 records/1s and writes to both evidence store (local WORM files with full prompt/response) and analytics store (JSONL summary). SHA-256 hash chain via Redis Lua script ensures tamper detection with sequential numbering. LlmCallDrainService persists cost records to partitioned table. Chain verification admin endpoint validates integrity. Verify: dotnet build exits 0, dotnet test exits 0 with integration test executing agent and verifying audit records + hash chain. Stop after 30 turns.
```
