# Audit & Evidence Subsystem on Azure for a Dual-Regulated (FCA/PRA + SARB/PA) Bank — Technical Reference (May 2026)

## TL;DR
- For tamper-evident LLM evidence, write full prompt/response payloads to **Azure Blob Storage with version-level WORM** (immutability policies + blob versioning), in storage accounts pinned to **UK South** and **South Africa North**; stream structured audit records via **Event Hubs Standard** (1 TU, ~$22/mo) into **Azure Data Explorer** (or a Microsoft Fabric Eventhouse) where the retention policy supports the 7+-year regulator window (ADX `SoftDeletePeriod` accepts up to 1,000 / 100,000 years).
- Capture LLM telemetry in the **Microsoft.Extensions.AI** (10.5.x) `IChatClient` pipeline via a `DelegatingChatClient` plus the Microsoft Agent Framework (1.0, GA 3 Apr 2026) `FunctionMiddleware` for tool calls; emit fire-and-forget records over a bounded `Channel<T>` to an Event Hub producer to keep the agent path non-blocking. Tamper detection uses a per-region/per-stream SHA-256 hash chain with monotonic sequence numbers, optionally rolled up daily into a Merkle root that is itself written to immutable Blob Storage.
- At the requested volume (≈15K events/day, ≈10K evidence files/day @50 KB), realistic monthly run-rate is roughly **$30–$80 / region** for Event Hubs + Blob WORM, but **ADX dominates at ≈$700–$1,200 / region / month** because of the minimum cluster floor — Azure SQL Hyperscale comes out cheaper at this volume (~$275/region) but loses KQL search ergonomics; using a shared ADX cluster across regions, or a Fabric Eventhouse on consumption-based capacity, is the pragmatic answer. Azure is independently attested against **FCA + PRA (UK)** and the **SEC 17a-4(f)/CFTC 1.31/FINRA 4511** WORM standards (Cohasset attestation), and the SA regions are POPIA-aligned for SARB/PA outsourcing; no "AI audit trail" service is built in, so the evidence subsystem is your responsibility on top.

---

## Key Findings

### 1. Topic-by-topic verdicts
| Area | Recommended choice | Why |
|---|---|---|
| Evidence files (full prompt+response JSON) | **Blob Storage GPv2 + version-level WORM, time-based retention 7y locked, container-level legal-hold capability** | Cohasset-attested for SEC 17a-4(f); blob-version policies allow per-record retention overrides without locking the whole container; max retention 146,000 days. |
| Region pinning | **Storage account `location = uksouth` / `southafricanorth`**, geo-redundancy ZRS or LRS only (no GRS to outside region) | Hard data-residency boundary; FG16/5 §"jurisdictions" + POPIA s72. |
| Streaming bus | **Event Hubs Standard, 1 TU, regional namespace** | 1 TU = 1 MB/s ingress, 1,000 events/s — comfortably above 0.17 events/s avg. Capture-to-Blob acts as a free secondary archive in Avro. |
| Long-term analytics | **Azure Data Explorer** (or Microsoft Fabric **Eventhouse** for consumption pricing) | KQL is the natural query language for compliance search; `SoftDeletePeriod` retention can be set to multi-year; retention vs. cache decoupled so 7-year cold storage is cheap. |
| LLM middleware | **Microsoft.Extensions.AI 10.5.2** + **Microsoft Agent Framework 1.0** | `DelegatingChatClient` is the canonical middleware base; MAF adds explicit `ChatMiddleware` / `FunctionMiddleware` / `AgentMiddleware` layers. |
| Tamper detection | Per-stream **SHA-256 hash chain with sequence numbers**, daily Merkle-root anchored into immutable Blob | Concurrency-safe, log-time verification, simple `System.Security.Cryptography.SHA256` implementation. |
| Compliance posture | Azure is in scope for FCA+PRA, EBA, ISO 27001/27017/27018/27701, ISO 42001, SOC 1/2/3, SEC 17a-4(f); SA North/West are POPIA-aligned. SS1/23 has no Azure-native control — your audit trail itself is the principal evidence artefact. |

### 2. The "no built-in AI audit trail" caveat
Neither Azure OpenAI / Foundry Models nor Azure AI Foundry currently emit a regulator-grade, immutable, end-to-end audit record of "what prompt was sent, what response came back, which tools fired, by which agent, on whose behalf". Microsoft Purview's *Data Security Posture Management for AI* and the new **Agent 365 Observability** (built on OpenTelemetry) cover Microsoft 365 Copilot and surface telemetry into Defender/Purview, but they are *not* substitutes for an SS1/23-grade model evidence store. You must build it.

---

## Details

### Topic 1 — Azure Blob Storage Immutable Storage (WORM)

**Capabilities (current as of 2026):**
- Two flavours: **Container-level WORM (CLW)** and **Version-level WORM (VLW)**. VLW requires blob versioning to be enabled on the storage account.
- **Time-based retention policy:** 1 day to 146,000 days (~400 years). Once *locked*, the retention can be extended up to 5 times but never shortened or deleted. Unlocked policies can be deleted/edited freely (use this for testing only).
- **Legal hold:** independent of time-based retention; remains in force until explicitly cleared. Both can apply simultaneously — a blob can only be deleted when *no* hold is active *and* the time-based retention has lapsed.
- **Allow protected appends:** `allowProtectedAppendWrites` (append blobs only) or `allowProtectedAppendWritesAll` (also block-blob blocks) — useful when streaming a continuous log into a single immutable blob.
- Cohasset Associates has independently attested the feature against **SEC 17a-4(f), CFTC 1.31(c-d), FINRA 4511**. The attestation letter is downloadable from the Service Trust Portal — keep a copy in your regulatory dossier.
- Not supported on hierarchical-namespace-disabled is fine; *not* supported on NFS 3.0 or SFTP-enabled accounts.

**Recommendation: VLW, not CLW.**
Audit evidence is naturally per-record (per LLM turn). Container-level retention forces every blob in the container to share the same period; version-level lets each evidence object carry its own per-blob policy (`SetImmutabilityPolicy` on a `BlobBaseClient`) and supports per-version legal holds for litigation/regulatory enquiry. VLW must be enabled on the **storage account at creation time** (or by container migration) — you cannot retro-fit account-level VLW. Plan this on day zero.

**Data residency configuration:**
```bicep
resource sa 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'auditevdgbukso01'
  location: 'uksouth'                 // or 'southafricanorth'
  sku: { name: 'Standard_ZRS' }       // ZRS keeps replicas in-region
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Disabled'   // private-endpoint only
    immutableStorageWithVersioning: {
      enabled: true                   // enables version-level WORM at account
    }
    isVersioningEnabled: true
  }
}
```
Geo-redundancy options: **LRS** keeps three copies in one DC, **ZRS** spreads across the three AZs in a single region. **Do not use GRS/RA-GRS** for residency-critical buckets — the secondary lives in another region (e.g. UK West, North Europe).

**SDK (use `Azure.Storage.Blobs` 12.27.x; `Azure.Identity` for managed identity):**
```csharp
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

public sealed class EvidenceWriter
{
    private readonly BlobContainerClient _container;
    private static readonly TimeSpan SevenYears = TimeSpan.FromDays(7 * 365 + 2);

    public EvidenceWriter(string accountUri, string containerName)
    {
        var svc = new BlobServiceClient(new Uri(accountUri), new DefaultAzureCredential());
        _container = svc.GetBlobContainerClient(containerName);
    }

    public async Task<string> WriteEvidenceAsync(string missionId, string agentName,
        ReadOnlyMemory<byte> jsonPayload, CancellationToken ct)
    {
        // Naming pattern: yyyy/MM/dd/{agent}/{missionId}/{guid}.json
        var now = DateTimeOffset.UtcNow;
        var blobName =
            $"{now:yyyy/MM/dd}/{agentName}/{missionId}/{Guid.NewGuid():N}.json";

        var blob = _container.GetBlobClient(blobName);

        // Upload with content hash for transport integrity
        var hash = System.Security.Cryptography.SHA256.HashData(jsonPayload.Span);
        var resp = await blob.UploadAsync(
            BinaryData.FromBytes(jsonPayload),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json",
                    ContentHash = hash
                },
                Metadata = new Dictionary<string, string>
                {
                    ["mission_id"]   = missionId,
                    ["agent"]        = agentName,
                    ["sha256"]       = Convert.ToHexString(hash),
                    ["created_utc"]  = now.ToString("O")
                }
            }, ct);

        // Apply per-version time-based immutability (locked)
        var versioned = _container.GetBlobBaseClient(blobName)
            .WithVersion(resp.Value.VersionId);

        await versioned.SetImmutabilityPolicyAsync(
            new BlobImmutabilityPolicy
            {
                ExpiresOn        = now.Add(SevenYears),
                PolicyMode       = BlobImmutabilityPolicyMode.Locked
            }, cancellationToken: ct);

        // (Optional) place a legal hold programmatically
        // await versioned.SetLegalHoldAsync(true, ct);

        return $"{blobName}@{resp.Value.VersionId}";
    }
}
```
RBAC: the writing principal needs **Storage Blob Data Contributor** *plus* the `Microsoft.Storage/storageAccounts/blobServices/containers/blobs/immutableStorage/runAsSuperUser/action` data action (built in to **Storage Blob Data Owner**). Block-blob policy *changes* on individual blobs are intentionally not audited (so an infinite number of policy edits is allowed); container-level policy changes are audited and retained for the lifetime of the container.

### Topic 2 — Azure Data Explorer (ADX) for long-term audit analytics

**Pricing model.** Three components:
1. **Engine VMs** (per-vCore-hour, e.g. Standard L8s / E-series) — the dominant cost.
2. **ADX markup** — service premium proportional to engine vCores; not charged on Dev/Test SKUs.
3. **Data Management cluster** — auto-sized 2-node ingestion pipeline, separately metered.
4. **Storage** — persistent extents in Azure Storage at LRS/ZRS rates (~$0.023/GB-month). An additional **7 days of retention buffer** and, when `Recoverability=Enabled` (default), **14 days of recoverability overhead** are silently appended on top of your declared retention. Hot SSD cache is part of the engine VM, not separately priced; the cache uses 95 % of local SSD for hot data, 5 % reserved for cold.

**Retention.** `SoftDeletePeriod` on the database/table/materialized-view retention policy can be set in days — the documented default is **1,000 years** in current docs (older docs say 100 years). **Setting 7 years (2,557 days) is well within bounds and standard for financial-services log archives.** Recoverability adds 14 days; if a regulator demands "no shadow copy" you set `Recoverability=Disabled`.

```kusto
.alter-merge database AuditDb policy retention
    softdelete = 2557d recoverability = enabled
.alter-merge table AgentAudit policy retention
    softdelete = 2557d recoverability = enabled
.alter table AgentAudit policy caching hot = 30d
```

**Event Hubs ingestion.** Connect via the native ADX *Event Hub data connection*: each connection pins one consumer group on one event hub. Microsoft's guidance:
- Use a **dedicated consumer group per ADX data connection** (don't share `$Default`).
- Co-locate the ADX cluster and the Event Hub in the same region, or use Premium/Dedicated Event Hubs to mitigate cross-region latency.
- Avoid uneven partition utilisation — discovery latency degrades on skewed partitions.
- Throughput: a single ADX data connection can process all partitions of one event hub in parallel.

**KQL patterns for compliance search:**
```kusto
// Date range + agent + mission
AgentAudit
| where ingestion_time() between (datetime(2026-04-01) .. datetime(2026-04-30))
| where AgentName == "kyc-screener-v3"
| where MissionId == "MSN-0042-AB"
| project ingestion_time(), Tenant, UserUpn, ModelName,
          InputTokens, OutputTokens, LatencyMs, ToolCalls, EvidenceBlobUri,
          PrevHash, RecordHash, SeqNo

// Tamper-detection scan: any gap in sequence numbers per stream?
AgentAudit
| summarize MinSeq = min(SeqNo), MaxSeq = max(SeqNo), Cnt = count() by StreamId
| where (MaxSeq - MinSeq + 1) != Cnt

// Hot-window query over cold data (e.g. 5-year-old investigation)
.alter table AgentAudit policy caching
    hot = 30d, hot_window = datetime(2021-06-01) .. datetime(2021-09-01)
```

**Cost estimate at ~15 K records/day @ ~3 KB.**
- Raw daily ingest ≈ 45 MB/day uncompressed → ≈ 16 GB/year → **≈ 115 GB over 7 years uncompressed** (~25–35 GB compressed in extents).
- Storage cost is therefore trivial (~ $1/month at year 7). The dominant cost is the **engine cluster floor**.
- Azure documentation reports list-price cost-per-GB-ingested clusters concentrated **2–10 SCUs/GB** (a "Sample Cost Unit" ≈ 1 ¢). At 16 GB/year, the cluster floor (smallest production-class cluster) is roughly **$700–$1,200 / month per region** — wildly oversized for the workload.
- **Practical mitigations:**
  1. Use a **shared ADX cluster** (one cluster per region or one global cluster with row-level security for region partitioning) and host all enterprise audit workloads on it.
  2. Use **Microsoft Fabric Eventhouse** (KQL on Fabric capacity) — same KQL, same retention semantics, **consumption-based capacity units**, which in 2026 is the cheapest path for sub-100-MB/day audit workloads.
  3. Use a **Dev/Test cluster** only for non-production; the regulator-facing system needs the Standard SLA.

### Topic 3 — Event Hubs

**Capture.** A managed consumer that lands every event into Blob/ADLS Gen2 in **Apache Avro** format on a configurable time/size window (`capture-interval` 60–900 s, `capture-size-limit` 10 MB–500 MB). Capture runs *outside* your TU egress quota — it never throttles producers. Useful as a free, immutable secondary archive of the raw event bus, separate from the ADX-curated table.

**ADX integration.** Two patterns:
- **Direct connector** (`Microsoft.Kusto/clusters/databases/dataconnections` of type `EventHub`) — recommended for the "live" ADX table.
- **Capture-to-Blob → Event Grid → ADX** — used when you want both Avro archive and analytics, or when you must replay.

**Partitioning.** Azure pricing is *not* per-partition; partition count is purely a parallelism knob. Recommendation for an audit bus:
- One namespace per region (data residency); one event hub per workload (e.g. `agent-audit`, `tool-invocations`).
- **Partition count = max parallelism you ever expect, ×1.5 headroom.** For 15K events/day there is zero throughput reason to exceed 4 partitions; choose 8 if you anticipate ×10 growth or want to parallelize multiple ADX consumers.
- **Partition key = `tenantId` + `agentId`** (concatenated, hashed) so that ordered replay works per-agent. Do *not* partition by region — region is already a namespace boundary.
- Standard tier partition count is fixed at create-time; only Premium/Dedicated allow dynamic scaling.

**Pricing at 15K events/day** (Standard tier, May 2026 list):
- 1 TU = `$0.03/hour × 730 = $21.90/month` (fixed).
- Ingress: 15,000 × 30 ÷ 1,000,000 = **0.45 M events/month × $0.028 = ~$0.013** (rounding error).
- Capture (optional): **$73/month per TU**.
- 84 GB storage included per TU per day; 7-day max retention on Standard. You will not exceed the 84 GB allowance.
- **Total Standard, with Capture, ~$95/mo per regional namespace; without Capture, ~$22/mo.**

**Data residency.** Namespaces are pinned to a region at creation and cannot be moved. Geo-replication (paired-namespace mirrors) is opt-in and the secondary's region is your choice — keep it disabled unless your DR plan accepts cross-jurisdiction replication, which generally fails FG16/5 and SARB outsourcing tests for production data.

### Topic 4 — Microsoft.Extensions.AI middleware patterns

**Stack (May 2026):**
- `Microsoft.Extensions.AI` **10.5.2** (latest stable on 5/2/2026)
- `Microsoft.Extensions.AI.Abstractions` 10.5.x
- `Microsoft.Extensions.AI.OpenAI` 10.5.1 (Azure OpenAI/OpenAI client adapter)
- **Microsoft Agent Framework 1.0** (GA 3 April 2026), which sits on top of `Microsoft.Extensions.AI`. MAF adds three explicit middleware layers: **`AgentMiddleware`** (per agent run), **`ChatMiddleware`** (per `IChatClient` call — same idea as `DelegatingChatClient`), and **`FunctionMiddleware`** (per tool/function invocation).

**Pipeline composition:**
```csharp
services.AddChatClient(builder => builder
    .UseDistributedCache()                 // cheap deterministic recall
    .UseLogging()
    .Use(next => new AuditingChatClient(next, auditSink))   // ← our middleware
    .UseFunctionInvocation()               // function-calling loop
    .UseOpenTelemetry(sourceName: "bank.agents",
        configure: c => c.EnableSensitiveData = false)      // OTel never sees raw prompts
    .Use(new AzureOpenAIClient(uri, cred).AsIChatClient(deployment)));
```
Order matters: a middleware registered earlier wraps middlewares registered later, so place the audit middleware **outside** (i.e. earlier than) `UseFunctionInvocation` if you want to capture the *outermost* request and response (compact, signed); place it *inside* if you want to capture each intermediate model round-trip in a multi-tool conversation. For SS1/23-grade evidence, capture *both* — register two `AuditingChatClient` instances at different layers, tagged `outer` and `inner`.

**Reference middleware (captures tokens, model name, latency, tool calls, hashes; non-blocking):**
```csharp
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

public sealed class AuditingChatClient(IChatClient inner, ChannelWriter<AuditRecord> sink)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var inputJson = JsonSerializer.SerializeToUtf8Bytes(messages);
        var inputHash = SHA256.HashData(inputJson);

        ChatResponse response;
        Exception? failure = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            // Best-effort: if the channel is full we DROP rather than block the agent.
            // The channel is configured BoundedChannelFullMode.DropOldest with a metric.
            sink.TryWrite(new AuditRecord(
                Timestamp:    DateTimeOffset.UtcNow,
                MissionId:    options?.AdditionalProperties?["missionId"]?.ToString() ?? "",
                AgentName:    options?.AdditionalProperties?["agentName"]?.ToString() ?? "",
                ModelId:      response?.ModelId ?? options?.ModelId ?? "",
                InputHash:    Convert.ToHexString(inputHash),
                OutputHash:   response is null ? "" :
                              Convert.ToHexString(SHA256.HashData(
                                  JsonSerializer.SerializeToUtf8Bytes(response.Messages))),
                InputTokens:  response?.Usage?.InputTokenCount  ?? 0,
                OutputTokens: response?.Usage?.OutputTokenCount ?? 0,
                LatencyMs:    stopwatch.ElapsedMilliseconds,
                ToolCalls:    ExtractToolCalls(response),
                Error:        failure?.GetType().FullName));
        }
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // IMPORTANT: streaming callers will skip GetResponseAsync entirely.
        // Buffer the stream and audit on completion.
        var buffered = new List<ChatResponseUpdate>();
        await foreach (var u in base.GetStreamingResponseAsync(messages, options, ct))
        {
            buffered.Add(u);
            yield return u;
        }
        // After enumeration, emit the audit record using buffered updates.
        // (omitted for brevity)
    }

    private static IReadOnlyList<string> ExtractToolCalls(ChatResponse? r) =>
        r?.Messages.SelectMany(m => m.Contents)
                   .OfType<FunctionCallContent>()
                   .Select(fc => fc.Name).ToArray() ?? Array.Empty<string>();
}
```

**Tool-call interception** — preferred path is **MAF's `FunctionMiddleware`** (per-tool, fine-grained, supports approval/sandboxing). The legacy/lower-level `IFunctionInvocationFilter` from Semantic Kernel still works for SK consumers but is deprecated in favour of MAF middleware in the merged framework. Use `FunctionMiddleware` to capture: function name, arguments JSON, return value, exception, and human-approval decision (record this — SS1/23 wants it).

**Non-blocking audit pattern (Channel<T> + Event Hub producer):**
```csharp
var channel = Channel.CreateBounded<AuditRecord>(
    new BoundedChannelOptions(capacity: 50_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false
    });

services.AddSingleton(channel.Writer);

// Background drain → Event Hubs (one BlockingCollection per partition not needed at 0.2/s)
services.AddHostedService(sp => new AuditDrainService(
    channel.Reader,
    sp.GetRequiredService<EventHubProducerClient>(),
    sp.GetRequiredService<ILogger<AuditDrainService>>()));
```
The drain service batches 100 records or 1 second worth (`EventDataBatch.TryAdd` until full, then `SendAsync`), retries with exponential backoff using `Polly`, and emits a counter for *dropped* records so a regulator-visible alarm fires the moment the audit pipeline is degrading. **Drops** must themselves be audited (synchronously, into a smaller "audit-of-audit-failures" Blob append blob in WORM).

### Topic 5 — Hash chain & tamper detection

**Per-stream hash chain (simple, scales to thousands of writers via stream sharding):**
```csharp
public sealed record AuditRecord(
    long SeqNo, string StreamId, DateTimeOffset Timestamp,
    string MissionId, string AgentName, string ModelId,
    string InputHash, string OutputHash,
    int InputTokens, int OutputTokens, long LatencyMs,
    IReadOnlyList<string> ToolCalls, string? Error,
    string PrevHash, string RecordHash);

public sealed class HashChainWriter
{
    private readonly object _gate = new();
    private long _seq;
    private string _prev = new('0', 64);  // genesis

    public AuditRecord Sign(AuditRecord skeleton)
    {
        lock (_gate)
        {
            var seq = ++_seq;
            var withPrev = skeleton with { SeqNo = seq, PrevHash = _prev };
            // Canonical JSON without RecordHash field
            var canonical = JsonSerializer.SerializeToUtf8Bytes(
                withPrev with { RecordHash = "" });
            var h = Convert.ToHexString(SHA256.HashData(canonical));
            _prev = h;
            return withPrev with { RecordHash = h };
        }
    }
}
```
**Why per-stream sequence numbers?** A single global lock won't scale across thousands of agent threads/regions. Define a **stream** as `{region}.{agentName}.{instanceId}` (or per-Event-Hubs-partition-key). Each stream maintains its own counter and prev-hash. A **stream registry** table in ADX records the genesis seqno and the latest seqno per stream so verification can spot gaps.

**Concurrent writes — three options, in increasing rigour:**
1. **Sequence number per stream** + per-stream lock (above). Adequate for single-process agents.
2. **Server-side ordering**: use the **Event Hubs sequence number** assigned on enqueue as the canonical order; client posts include only the local seqno and prev-hash, but verification reorders on the server. Trade-off: chain hash must be computed by the *consumer* in a single-threaded materialiser, which then writes the canonical chained record into ADX/Blob.
3. **Merkle tree**: each writer commits a leaf with `H(record)`; once per minute (or per N records), a coordinator builds a Merkle tree, writes the **root** to an immutable append blob (`$blobchangefeed`-style) and to ADX. Verification proves inclusion in O(log N). This is the recommended pattern when many writers must commit independently and a regulator wants a single anchor commitment per period — it is the model used by Certificate Transparency and Trillian.

**Daily anchoring (recommended hybrid):** stream-local hash chains for cheap intra-day verification, plus a **daily Merkle root** computed over all records of the day, written to a separate WORM blob `merkle-roots/{yyyy-MM-dd}.json` *and* notarised by including the root in the next day's first record. This gives both per-record proofs and daily "tree heads" — mirrors the Bank of England SS1/23 expectation that "documented evidence of model decisions, changes, and validation outcomes" be inspectable.

**Verification algorithm** (linear pass):
```csharp
public static bool VerifyChain(IEnumerable<AuditRecord> orderedByStreamSeq)
{
    string prev = new('0', 64);
    long expected = 0;
    foreach (var r in orderedByStreamSeq)
    {
        if (r.SeqNo != ++expected) return false;             // gap
        if (!string.Equals(r.PrevHash, prev, StringComparison.OrdinalIgnoreCase)) return false;
        var canonical = JsonSerializer.SerializeToUtf8Bytes(r with { RecordHash = "" });
        var h = Convert.ToHexString(SHA256.HashData(canonical));
        if (!string.Equals(r.RecordHash, h, StringComparison.OrdinalIgnoreCase)) return false;
        prev = r.RecordHash;
    }
    return true;
}
```
For Merkle verification, use a standard CT-style inclusion proof (`audit_path` of sibling hashes from leaf to root); arxiv:2511.17118 ("Constant-Size Cryptographic Evidence Structures for Regulated AI Workflows", Nov 2025) formalises this and provides a reference library suitable for adaptation.

### Topic 6 — Azure regulatory certifications and AI governance

**Certifications relevant to FCA/PRA (UK):**
- Microsoft publishes a dedicated **"FCA and PRA (UK)"** Azure compliance offering on Microsoft Learn (`learn.microsoft.com/azure/compliance/offerings/offering-fca-pra-uk`), including: a Microsoft compliance summary aligned to **FG 16/5** (FCA finalised guidance for cloud outsourcing) and the **PRA Outsourcing/Notifications** rulebook parts; SOC 1/2/3 Type II reports; ISO 27001/27017/27018/27701 certificates; **CSA STAR Attestation/Certification**.
- The **Cohasset Associates** WORM attestation explicitly covers SEC 17a-4(f), CFTC 1.31(c-d), FINRA 4511. Many UK and SA regulators accept these as functional evidence even though they aren't UK-specific.
- **Operational Resilience (FCA PS21/3, PRA PS6/21)** is a customer obligation — Microsoft publishes shared-responsibility content but firms must still set impact tolerances.
- **Critical Third-Party (CTP) regime** — the UK CTP rules give FCA/PRA/BoE direct oversight of designated cloud providers; Azure has been operating against the regime since 1 Jan 2025 and FCA notification rules for material third-party arrangements take effect **18 March 2027**. New deployments should design with CTP designation in mind.

**Certifications relevant to SARB/PA (South Africa):**
- Azure South Africa North (Johannesburg) and South Africa West (Cape Town) regions are positioned by Microsoft as **POPIA**-aligned and serve as the data-residency boundary for personal information of SA data subjects.
- SARB's **Directive on cloud computing and offshoring of data (2018)** and the **Joint Standard on Outsourcing** drive a notification-/approval-based regime; Microsoft publishes a SA financial-services compliance page that maps controls to FSR Act, FAIS, FICA. There is no "SARB certification" of Azure — compliance is built per-firm using the standard SOC/ISO attestations plus Microsoft's contractual addenda.
- Cohasset's WORM attestation is again the proxy for "non-rewriteable, non-erasable" evidence, which SARB inspectors recognise via the SEC 17a-4(f) language.

**Built-in Azure AI governance — what exists and what doesn't:**
- **Microsoft Purview Data Security Posture Management for AI (DSPM for AI):** discovers AI usage in tenant (Copilot, Azure AI Foundry, third-party SaaS via Defender for Cloud Apps), classifies data flowing into prompts, applies sensitivity labels and DLP. **Generally available** as of 2026 (with new partner-extension support). Useful for *data*-side controls; not a model-decision audit trail.
- **Microsoft Purview Compliance Manager** ships AI regulatory templates (EU AI Act, NIST AI RMF). It does **not** ship an SS1/23 template as of May 2026; you would build one as a custom assessment.
- **Microsoft Purview Audit (M365 Advanced Audit)** retains user/admin events for up to 10 years with an add-on; this is the right place for *human* admin actions on the audit pipeline (key rotations, policy edits) but not LLM events.
- **Azure AI Foundry / Azure OpenAI**: provides per-deployment metrics (tokens/min, RAI filter triggers) and content-filter logs, but no immutable per-conversation audit; **Foundry Models** and **Foundry Agent Service** rely on customer-side OpenTelemetry. Compliance Manager now syncs evaluation results from AI Foundry, but again, evaluation ≠ audit.
- **Microsoft Agent 365 Observability** (preview/early-GA in 2026) emits OTel-based telemetry into Defender + Purview. Designed for Microsoft 365 Copilot agent ecosystem, not custom .NET agents — but the same OTel conventions can be reused.
- **Azure Policy:** controls deployment-time configuration. Useful policies for an audit subsystem: deny storage accounts without `immutableStorageWithVersioning`, deny non-WORM containers in regulated subscriptions, require private endpoints, enforce region pinning to UK South / SA North, audit Event Hubs Capture being enabled. Use the built-in **ISO 27001:2013 Regulatory Compliance** initiative as your starting policy set and overlay custom definitions.

**SS1/23 (PRA) and Azure — practical mapping:**
SS1/23 is principles-based (Principles 1–5: identification, governance, development/validation, performance monitoring, model risk reporting). Azure has **no service that directly attests to SS1/23**. Where Azure helps:
| SS1/23 expectation | Azure-side mechanism |
|---|---|
| Centralised model inventory (incl. AI/ML, GenAI, vendor) | Tag every Azure OpenAI / Foundry deployment with `model-id`, `risk-tier`, `business-owner`; export via Azure Resource Graph nightly into your inventory tool. |
| Documentation of decisions, changes, validation | The audit subsystem **is** the evidence: every prompt+response in WORM, every tool call, every policy change in `$logs` and Purview Audit. |
| Performance monitoring & ongoing validation | Azure Monitor + ADX dashboards over the AgentAudit table; Application Insights; Azure AI Foundry evaluation runs synced into Compliance Manager. |
| Independent validation; user/developer separation | Azure RBAC + PIM for the model-validation team; Azure Policy denying ML-team writes to the audit subscription. |
| Third-party/vendor model controls | Outsourcing addenda from Microsoft (Trusted Cloud); contracts inherit FCA FG16/5 audit/access rights. |

### Topic 7 — Cost estimation (May 2026 list prices, USD, per region)

**Assumptions:** 15K events/day, 10K evidence files/day @ 50 KB, 7-year retention. UK South / SA North list prices are slightly higher than US East but within ~5–10 %; use US figures as a planning floor.

| Component | Configuration | Monthly cost (steady-state) | 7-year accumulated |
|---|---|---|---|
| **Event Hubs Standard** | 1 TU + Capture | $21.90 (TU) + $73 (Capture) + $0.013 (events) ≈ **$95** | ~$8,000 |
| **Event Hubs Standard** (no Capture) | 1 TU only | **~$22** | ~$1,850 |
| **Immutable Blob (evidence)** | 10K × 50 KB/day = 0.5 GB/day; Hot first 30 d, Cool thereafter; LRS | Year 1: ~$8/mo. Year 7: storage 1.26 TB × ~$0.011 (blended Cool, accounting for early-deletion holds) ≈ **$15/mo + $1.50/mo writes** | ~$700–$1,000 |
| **ADX cluster** (small Standard SKU shared across both regions) | 2× D11_v2 engine, 2× D11_v2 DM, 30-day hot cache | **~$700–$1,200/mo per cluster** (cluster floor dominates) | ~$60,000–$100,000 |
| **ADX storage** | 35 GB compressed @ year 7, +14d recoverability +7d buffer | **<$2/mo** | ~$100 |
| **Microsoft Fabric Eventhouse** (alternative) | F2/F4 capacity sized to workload (consumption) | **~$260–$520/mo** (F2 ≈ $262, F4 ≈ $525 list) | ~$22,000–$44,000 |
| **Azure SQL Hyperscale** (alternative) | 2 vCore Standard-Series + 50 GB storage | **~$275/mo** ($266 compute @ $0.366/hr × 730 + $12 storage @ $0.25/GB-mo) | ~$23,000 |

**ADX vs Azure SQL Hyperscale at this volume — verdict:**
- *Pure cost over 7 years:* Hyperscale is **3–4× cheaper** than dedicated ADX for this workload, comparable to a Fabric Eventhouse F2.
- *Query ergonomics:* ADX/Eventhouse wins decisively for compliance search (full-text on JSON columns, `parse_json`, time-series operators, materialised views), and KQL is the same language used in Sentinel and Defender, which auditors increasingly know.
- *Schema flexibility:* ADX absorbs schema drift (new tool fields, new model attributes) without DDL pain. SQL requires migrations.
- *Recommendation:* If the bank already runs an ADX cluster (e.g. for Sentinel/log-analytics consolidation), **piggyback the audit DB onto it** and pay only the marginal storage. If not, **start with Microsoft Fabric Eventhouse** (consumption pricing, KQL parity) and graduate to a dedicated ADX cluster only when daily ingest crosses ~5 GB. Use **Azure SQL Hyperscale only if** the firm has an existing T-SQL audit-tooling investment; the savings vanish the moment compliance starts asking ad-hoc full-text questions.

**Combined steady-state monthly cost target, both regions, no shared cluster:**
- Optimistic (Eventhouse F2 per region, no Capture): 2 × ($22 + $15 + $262) ≈ **$598/month**
- Realistic (Eventhouse F2 + Capture): 2 × ($95 + $15 + $262) ≈ **$744/month**
- Worst (dedicated ADX per region + Capture): 2 × ($95 + $15 + $900) ≈ **$2,020/month**

Per-record cost at the realistic figure: $744 ÷ (2 × 15,000 × 30) = **~$0.00083 per audited turn**, or roughly **$0.83 per 1,000 turns** — well within tolerance for a regulated bank workload.

---

## Caveats

- **Microsoft Agent Framework version movement.** Several technical references for MAF middleware in 2026 (DevLeader, the-runtime.dev) describe APIs that the framework itself still flagged as preview/RC1 earlier in 2026; MAF 1.0 GA shipped 3 April 2026. Pin `Microsoft.Agents.AI` package versions explicitly and re-validate `DelegatingChatClient` / `FunctionMiddleware` signatures against the GA release notes before locking your audit middleware. The `Microsoft.Extensions.AI` 10.5.x APIs used here are stable.
- **ADX retention default.** Microsoft documentation in different places states the default `SoftDeletePeriod` as either **100 years** or **1,000 years** depending on the doc version; both are far longer than 7 years and the practical answer is to set retention explicitly. Verify against your live cluster (`.show database X policy retention`).
- **ADX cluster cost figures** above are *list-price estimates* for small Standard-tier clusters and depend heavily on chosen SKU and reservations; the Azure pricing calculator should be used for committed numbers. The Microsoft published "cost per GB ingested" study (June 2025 snapshot) ranges 2–10 SCUs/GB across real customer clusters, so per-GB figures published by third-party blogs (e.g. EPC Group) are illustrative only.
- **Azure OpenAI region availability.** South Africa North hosts a subset of Azure OpenAI / Foundry models (gpt-5 family registration is required, gpt-4.1 family is broadly available); UK South has wider coverage. For dual-jurisdiction routing, the agent platform must check model availability per region or accept routing to a Data-Zone deployment for latency-tolerant cases — but **DataZone deployments cross jurisdiction boundaries and may breach SARB outsourcing notification requirements**; default to "global standard" off for production agents and pin to the regional resource.
- **SS1/23 scope.** SS1/23 formally applies to PRA-regulated banks/building-societies/PRA-designated investment firms with internal-model permissions for capital. Even firms outside scope (which includes many South African subsidiaries operating only in SA) are encouraged by the PRA to adopt the principles; treating the audit subsystem as if it were in scope is the prudent default for a dual-regulated UK/SA group.
- **"Built-in AI audit trail" claims.** Marketing material occasionally implies that Azure AI Foundry, Purview, or Agent 365 Observability provide regulator-grade AI audit trails. They provide *signal* (telemetry, DSPM findings, M365 admin audit) but **none provide an immutable, hash-chained, per-conversation evidence record** that a model-validation team can replay 7 years later. Building this layer (Topics 1, 4, 5 above) is unavoidable.
- **Cohasset attestation.** The published WORM attestation maps to *US* securities regulations; FCA, PRA and SARB have not issued equivalent named attestations against Azure WORM. In practice the Cohasset letter, plus ISO 27001/27017/27018/27701 and SOC reports, plus Microsoft's FCA/PRA mapping, is what regulated firms are presenting in 2026 supervision visits — but a firm-level legal sign-off is required; this document is not legal advice.
- **Pricing.** All USD figures are May 2026 Microsoft Azure list prices (Pay-As-You-Go, US East / global); UK South and South Africa North are typically 5–15 % higher and SA North in particular has a smaller services catalogue. Reservations (1y/3y) and Microsoft Customer Agreement discounts are not included — typical bank discounts are 15–35 %, materially changing the ADX-vs-Hyperscale break-even.