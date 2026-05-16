# Workflow Engine Comparison for Long-Running AI Agent Workflows on Azure Container Apps + .NET Aspire

*Regulated financial services context — research as of 10 May 2026*

## TL;DR

- **For a regulated bank running on ACA + .NET Aspire today, the strongest default is a hybrid: Microsoft Agent Framework (MAF) workflows for the agent graph, hosted on the Durable Task SDK (`Microsoft.DurableTask.Worker.AzureManaged`) with the Azure-managed Durable Task Scheduler (DTS) as the durable backend.** This is the only fully-supported, GA, .NET-native combination that is purpose-built for Azure Container Apps, runs in-process inside an ASP.NET Core / Aspire app, and has a Microsoft support ticket path. The MAF↔DTFx integration (`Microsoft.Agents.AI.DurableTask` 1.4.0-preview) is still preview, so for production today you typically run MAF *workflows* under Durable Task SDK orchestrators rather than relying on MAF's built-in supersteps for durability.
- **Azure Durable Functions (.NET isolated)** is GA, mature, and uses the same Durable Task primitives — but it is tied to the Azure Functions host, which is an awkward fit for an Aspire-orchestrated Container App and forces an out-of-process serverless model rather than embedding in an ASP.NET Core process. **Self-hosted DTFx (the legacy Azure/durabletask repo)** is community-maintained, has no Microsoft support, and is explicitly deprecated by Microsoft in favour of the Durable Task SDKs for new projects.
- **Temporal .NET SDK** (Temporalio 1.13.0) is the most feature-complete durable-execution engine — best signals/queries/updates, native cron schedules, true child workflows, deepest versioning — but self-hosting a production Temporal cluster on Azure requires running a separate stateful service (Temporal Server + PostgreSQL/Cassandra + Elasticsearch) with known Azure gotchas (TLS, BTREE_GIN, archival). For a bank that already operates Postgres + AKS this is viable; for an Aspire/ACA-only shop the operational tax is significant, and Temporal Cloud (~US$200/month minimum) is the only path that avoids it.

---

## Key Findings

### 1. State of MAF and the durability story (May 2026)

- **GA status.** Microsoft Agent Framework reached **1.0 GA on 3 April 2026**; current `Microsoft.Agents.AI` is **1.5.0** on NuGet. MAF is the merged successor to Semantic Kernel + AutoGen; MIT-licensed.
- **Workflow model.** `WorkflowBuilder` produces a directed graph of *Executors* connected by typed *Edges*; execution is a Pregel-style **Bulk Synchronous Parallel** model with **supersteps** and a synchronization barrier between them. Agents (`AIAgent`) can be used directly as executors via `AsAIAgent`.
- **Built-in durability is checkpoint-only, not durable execution.** MAF ships `CheckpointManager` / `CheckpointStorage` with `InMemoryCheckpointStorage` (default; loses everything on restart), `FileCheckpointStorage`, and (Python) `CosmosCheckpointStorage`. .NET has the same abstraction but no first-party Cosmos provider yet — you write your own to plug into Azure persistence. Checkpoints are taken **at superstep boundaries**, not at activity boundaries: if a superstep contains three executors and the third fails, **all three re-execute on resume**. There is no distributed lock or fencing token, so two workers can resume the same checkpoint simultaneously (Diagrid analysis, March 2026).
- **The official answer to "real durability" is the Durable Task extension.** `Microsoft.Agents.AI.DurableTask` (1.4.0-preview.260505.1, last updated 5 May 2026 — **still preview**) lets you keep the same `WorkflowBuilder` definition and host it on Durable Task Scheduler, getting durable orchestrations, durable entities for stateful agents, and the DTS dashboard. Each MAF executor becomes a DTS activity (`dafx-…` prefix). A separate `Microsoft.Agents.AI.Hosting.AzureFunctions` package adds Azure Functions hosting on top.
- **HITL.** MAF models pause-for-human via `RequestPort` (which emits `RequestInfoEvent`) and via tool-approval middleware. When hosted on the durable extension, the framework auto-generates HTTP endpoints for listing pending requests and submitting responses; pending requests are persisted in the checkpoint and re-emitted on resume.

### 2. State of Durable Functions / Durable Task in May 2026

- **Durable Functions .NET isolated** is the supported model. The in-process model **reaches end of support 10 November 2026** — migration is mandatory. Current packages: `Microsoft.Azure.Functions.Worker.Extensions.DurableTask` **1.16.3** (March 2026), `Microsoft.Azure.WebJobs.Extensions.DurableTask` 3.10.1.
- **Durable Task SDKs** (the standalone, no-Functions library) are the recommended path for Azure Container Apps. Current: `Microsoft.DurableTask.Worker.AzureManaged` and `Microsoft.DurableTask.Client.AzureManaged` **1.19.0** (May 2026). They run in any .NET host (ASP.NET Core, console, worker service) and integrate with DI.
- **Durable Task Scheduler (DTS)** — the managed Azure backend — is the recommended store: **Dedicated SKU GA** since mid-2025; **Consumption SKU reached GA on 31 March 2026** (pay-per-action, ≤500 actions/sec, 30-day retention; Dedicated supports 2 000 actions/sec/CU, 50 GB/CU, up to 90-day retention, and HA with ≥3 CUs). Authentication is Entra ID + RBAC, no SAS keys. A Docker emulator (`mcr.microsoft.com/dts/dts-emulator`) is available for local dev and integrates with Aspire via the `CommunityToolkit.Aspire.Hosting.DurableTask` package.
- **Legacy DTFx** (`Microsoft.Azure.DurableTask.Core` 3.7.1, `Azure/durabletask` GitHub) is explicitly labeled by Microsoft as community-maintained, **not officially supported** (no support tickets), and Microsoft now recommends the Durable Task SDKs + DTS for any new project. SQL provider (`durabletask-mssql`) and Azure Storage / Service Bus / Netherite providers exist but Netherite support ends 31 March 2028.
- **Activity timeouts and HITL.** `WaitForExternalEvent<T>(name, timeout, cancelToken)` is now first-class (the wrapper pattern from older blogs is built-in). Durable timers in .NET support arbitrarily long durations (the framework chains shorter timers internally). Per-activity retry policies (`RetryPolicy` / `TaskOptions`) support `MaxNumberOfAttempts`, `FirstRetryInterval`, `BackoffCoefficient`, `MaxRetryInterval`, `RetryTimeout`, and a custom `Handle` callback.

### 3. State of Temporal .NET SDK (May 2026)

- **`Temporalio` 1.13.0** on NuGet (latest), `Temporalio.Extensions.Hosting` 1.11.1 with `AddHostedTemporalWorker` for generic-host / DI integration. SDK supports .NET Framework 4.6.2+, .NET Core 3.1+, .NET Standard 2.0.
- The SDK ships a **native Rust core (`temporalio_sdk_core_c_bridge`)**. RIDs supported: linux-x64/-arm64/-musl-x64/-musl-arm64, osx-x64/-arm64, win-x64/-arm64. On Alpine you need musl builds; on Windows containers you may need the Visual C++ runtime; on AKS/Lambda there's a known `SSL_CERT_FILE` workaround.
- Temporal **Schedules** are GA (replacing legacy Cron Jobs) — pause/resume/update/backfill via `TemporalClient.CreateScheduleAsync` and the `ScheduleHandle`.
- **Temporal Cloud pricing (March 2026):** Free Dev tier; Growth ≈$200/month (1 M actions); Business ≈$2 000/month; additional actions ~$0.00025 each. Self-hosted Server is MIT-licensed and free.
- **Self-hosted on Azure.** Production deployment on AKS via Helm (`temporalio/helm-charts`), backed by PostgreSQL/MySQL/Cassandra + Elasticsearch for visibility. Documented Azure pain points: no archival to Azure Blob (only S3/GCS), AKS + Azure Flexible Server PostgreSQL requires forced TLS handling, and visibility on Postgres requires the **BTREE_GIN** extension (not enabled by default in Azure flexible-server). It is not a natural fit to host the Temporal Server itself in an Azure *Container App* (it expects long-lived stateful pods with peer discovery on a known IP and ulimit tuning).

### 4. Architectural fit for ACA + .NET Aspire

| Trait | MAF Workflows (in-proc) | MAF + DurableTask ext. | Durable Functions (isolated) | DTFx (self-hosted) | Durable Task SDK + DTS | Temporal (.NET) self-host | Temporal Cloud |
|---|---|---|---|---|---|---|---|
| Embeds in ASP.NET Core / Aspire process | Yes | Yes (worker host) | No (Functions host) | Yes | Yes | Yes (worker only) | Yes (worker only) |
| Requires separate cluster/server | No | Yes (DTS — managed) | No (Functions) but needs storage | Needs storage backend (Storage / SQL / Service Bus) | Yes (DTS — managed) | Yes (Server + DB + ES) | No (managed) |
| Aspire integration today | Native via NuGet | `CommunityToolkit.Aspire.Hosting.DurableTask` (preview) | Functions-with-Aspire (Aspire 13) | None first-party | Same as DurableTask | Community packages (`rebecca-powell` blog, `temporalite`) | Same as self-host |
| Latest .NET package (May 2026) | `Microsoft.Agents.AI` 1.5.0 (GA) | `Microsoft.Agents.AI.DurableTask` **1.4.0-preview** | `…Worker.Extensions.DurableTask` 1.16.3 (GA) | `…DurableTask.Core` 3.7.1 (GA but unsupported) | `Microsoft.DurableTask.Worker.AzureManaged` 1.19.0 (GA) | `Temporalio` 1.13.0 (GA) | same |
| Microsoft support contract | Yes (MAF GA) | No (preview) | Yes | **No** (best-effort) | Yes | No (third-party) | Temporal Inc. SLA |

---

## Details

### A. State persistence backend

| Engine | Default backend | Production options on Azure | Where state lives |
|---|---|---|---|
| MAF (in-proc supersteps) | `InMemoryCheckpointStorage` | `FileCheckpointStorage`, custom (Cosmos in Python today); .NET requires custom impl for managed services | One `WorkflowCheckpoint` per superstep (graph hash, in-flight messages, executor state) |
| Durable Functions (.NET isolated) | DTS (recommended) or Azure Storage (Tables/Blobs/Queues), MSSQL, Netherite (EOL 2028) | DTS Consumption/Dedicated, Azure SQL (`durabletask-mssql`) | Event-sourced history per orchestration instance |
| DTFx (legacy) | `DurableTask.AzureStorage`, `DurableTask.AzureServiceBus`, `DurableTask.SqlServer`, `DurableTask.EFCore`, `DurableTask.Emulator` | Self-managed Azure SQL, Azure Storage, Service Bus | Same event-sourced history |
| Durable Task SDK | DTS only (`AzureManaged`) | DTS Consumption (≤500 actions/s, 30-day retention) or Dedicated (≥2 000 actions/s/CU, ≤90-day, optional HA ≥3 CUs) | DTS internal store; in-memory for hot state, persistent for recovery |
| Temporal | Pluggable | PostgreSQL Flexible Server (with BTREE_GIN), MySQL, or Cassandra + Elasticsearch on AKS; Temporal Cloud as managed alternative | Workflow event history (50 k events / 50 MB hard cap before Continue-As-New required) |

### B. Durability, restarts, and crash semantics

| Requirement | MAF in-proc | MAF + DurableTask | Durable Functions | DTFx | DT SDK + DTS | Temporal |
|---|---|---|---|---|---|---|
| Survives ACA pod recycle / deploy | Only if you wrote a durable `CheckpointStorage` and run the `Runner` recovery code yourself | Yes — DTS owns the queue and history | Yes | Yes (depends on backend) | Yes | Yes |
| Recovery granularity | Last completed superstep (whole superstep re-runs) | Activity-level (DTS replay) | Activity-level | Activity-level | Activity-level | Activity-level (replay from event history) |
| Distributed lock / fencing on resume | None — two pods can resume same checkpoint | Yes (DTS partitions/leases) | Yes | Yes | Yes | Yes (sticky task queues + history fencing) |
| What happens if host crashes mid-workflow | State up to last superstep is on disk if you configured persistent storage; in-flight work is lost and re-run on resume; risk of duplicate side-effects | DTS re-dispatches the in-flight activity to another worker, replays orchestrator from history | Same as DT SDK | Same | DTS re-dispatches; orchestrator replays deterministically | Server detects worker timeout, replays workflow on a new worker |
| Max workflow duration | Bounded by checkpoint store; no engine cap | Effectively unlimited (DTS retention 30/90 days for *closed* runs; *running* instances are not aged out) | Same | Same | Same | Effectively unlimited via Continue-As-New (bounded by 50 k event / 50 MB per run) |

### C. Functional capability matrix vs the bank's 10 requirements

| # | Requirement | MAF in-proc | Durable Functions (.NET isolated) | DTFx (self-hosted) | DT SDK + DTS | Temporal (.NET) |
|---|---|---|---|---|---|---|
| 1 | Durable execution across ACA pod restarts | ⚠️ Only with custom CheckpointStorage; superstep granularity, no fencing | ✅ | ✅ (operationally heavier) | ✅ | ✅ |
| 2 | HITL pause minutes–24 h, resume exactly | ✅ via `RequestPort` + checkpoint; in-proc loses pending requests on crash unless DTS extension is used | ✅ `WaitForExternalEvent<T>(name, timeout)` | ✅ | ✅ | ✅ Signals/Updates + durable Timers; cleanest model |
| 3 | Cron-triggered runs | ❌ (no native scheduler — use external trigger) | ✅ Functions Timer trigger; DTS scheduling preview in .NET SDK | ❌ (BYO) | ✅ DTS Scheduling (preview, .NET SDK only) | ✅ Schedules GA: pause/resume/backfill/update |
| 4 | Child workflows independently retriable | Sub-workflows via `AddSubWorkflow`; not independently durable in-proc | ✅ Sub-orchestrations with retry policy; tags supported (1.19) | ✅ | ✅ | ✅ Best-in-class: Parent-Close-Policy, Continue-As-New, Updates |
| 5 | Signals + queries from external systems | Events emitted, responses via `SendResponseAsync`; no built-in query API | ✅ `RaiseEventAsync`; query via instance status (no general query) | ✅ | ✅ Same; DTS dashboard exposes raise-event/terminate over RBAC | ✅ Signals + Queries + Updates (synchronous/validated) |
| 6 | Hierarchical timeouts (workflow 30 d, activity 10 m, HITL 4 h / 24 h) | Per-superstep timeout via your code; no engine timeout hierarchy | ✅ Activity-level via `RetryPolicy`/timer; orchestration timeout via `CreateTimer`; **note**: Functions host imposes per-activity execution cap (5 min Consumption / 30 min default Dedicated; configurable up to unlimited on Premium/Flex) | ✅ Activity timeouts + custom timers | ✅ Same primitives, no Functions-host cap (you control the worker) | ✅ Cleanest: `WorkflowExecutionTimeout`, `WorkflowRunTimeout`, `WorkflowTaskTimeout`, plus per-activity `ScheduleToCloseTimeout`, `StartToCloseTimeout`, `HeartbeatTimeout` |
| 7 | Per-activity retry policies | Manual in executor code | ✅ `RetryPolicy(maxAttempts, firstRetryInterval, backoffCoeff, maxRetryInterval, retryTimeout, Handle)` | ✅ | ✅ Same | ✅ Equivalent + non-retryable error types + exponential jitter |
| 8 | Budget check before each step + terminate | Implementable as middleware between supersteps | Implementable in orchestrator code | Same | Same | Implementable as workflow interceptor / middleware; workflow can `ContinueAsNew` or fail explicitly |
| 9 | Auditable state-transition log | Workflow events streamed; no built-in audit store | ✅ DTS dashboard + history; Application Insights | History in storage backend | ✅ DTS dashboard with Gantt/sequence charts, Entra ID + RBAC, role-scoped per task hub | ✅ Web UI + event history; Principal field (server-attributed identity) GA in 1.31 |
| 10 | Concurrency control N per mission | DIY (semaphore in graph) | `LockAsync` via Durable Entities; per-instance singleton | Same | Same | `WorkflowIdReusePolicy`, task-queue rate limits, custom slot suppliers; cleanest |

### D. Deployment complexity on Azure Container Apps

| Engine | What you deploy | Aspire integration | Operational footprint |
|---|---|---|---|
| MAF in-proc | Single ACA replica running ASP.NET Core + MAF. Add a CheckpointStorage. | `Microsoft.Agents.AI` works directly; no extra Aspire resource | Lowest. Not durable enough for regulated production on its own. |
| MAF + DurableTask | ACA app (worker + client) + DTS Azure resource (managed) | `CommunityToolkit.Aspire.Hosting.DurableTask` provides `AddDurableTaskScheduler` + `AddTaskHub` and emulator wiring | Low. DTS is fully managed; identity-based auth; emulator runs in Docker for local. |
| Durable Functions (.NET isolated) | Function App (Functions runtime container or ACA-hosted Functions) | Aspire 13+ has `Azure Functions with Aspire` support, but you're tied to the Functions host process model | Medium. Forces you to split mission control between Functions and ACA app, which is awkward in Aspire's project graph. |
| DTFx self-hosted | ACA app embedding `DurableTask.Core` + chosen storage NuGet (Azure Storage / SQL / Service Bus) | None first-party | Medium-high. You own scaling, partition leases, dashboard, upgrades, and have **no Microsoft support**. |
| Durable Task SDK + DTS | ACA worker + ACA client + DTS Azure resource | `CommunityToolkit.Aspire.Hosting.DurableTask` | Low. Built-in KEDA scaler `azure-durabletask-scheduler` autoscales replicas by orchestration/activity backlog. |
| Temporal self-hosted | AKS cluster with Helm chart (frontend, history, matching, worker services) + PostgreSQL + Elasticsearch + Web UI; ACA worker process consumes Temporal via gRPC | Community samples (Aspirate-generated K8s manifests, `temporal server start-dev` container resource) | High. Temporal Server itself is *not* a good fit for ACA — needs stateful pods, ulimit, sticky IPs. Run on AKS, not ACA. |
| Temporal Cloud | ACA worker only; cluster managed by Temporal Inc. | Same Aspire community pattern, point to `*.tmprl.cloud` endpoint with mTLS | Lowest of Temporal options, but ~$200/month minimum and data-residency review required for a regulated bank. |

### E. Pause-for-human-approval pattern (specific implementation)

| Engine | Mechanism | Persistence of pending request | Resume call | Notes |
|---|---|---|---|---|
| MAF in-proc | `RequestPort.Create<TReq,TResp>("Approve")` emits `RequestInfoEvent` | Yes if `CheckpointStorage` is durable; in-memory loses it | `handle.SendResponseAsync(req.CreateResponse(value))` | Auto-generated HTTP endpoints when hosted on Azure Functions |
| Durable Functions / DT SDK | `await context.WaitForExternalEvent<T>("ApprovalDecision", TimeSpan.FromHours(24))` raises `OperationCanceledException` on timeout; resume with `client.RaiseEventAsync(instanceId, "ApprovalDecision", payload)` | Yes (in DTS / storage) | HTTP API or `DurableTaskClient` | Can race timer + event with `Task.WhenAny` for escalation paths (4 h → 24 h) |
| DTFx | Same pattern as DT SDK; you build your own HTTP front-end | Yes | `TaskHubClient.RaiseEventAsync` | No built-in dashboard |
| Temporal | Signal handler + `Workflow.WaitConditionAsync(() => approved)`, or **Update** (synchronous, validated, returns response). For timeout escalation, use `Workflow.DelayAsync(4h)` or cancellation-token race. | Yes (event history) | `client.GetWorkflowHandle(id).SignalAsync(wf => wf.ApproveAsync(input))` | Updates beat Signals when you need the caller to receive a validated response |

### F. Calling MAF agents from each engine

- **MAF in-proc:** Native — agents *are* executors via `AsAIAgent`. Tool-approval middleware integrates directly.
- **MAF + DurableTask extension:** `context.GetAgent("AgentName")` returns a `DurableAIAgent` wrapper that ensures agent calls are checkpointed. `AgentSession`/`AgentThread` is durably persisted by Durable Entities.
- **Durable Functions / DT SDK:** Inject `IAIAgent` into an *activity* function, not the orchestrator (orchestrator must remain deterministic — no LLM calls). Activity wraps `agent.RunAsync(...)` and the orchestrator calls it via `CallActivityAsync`. With the durable extension above, you can also use `DurableAIAgent` directly inside the orchestrator.
- **DTFx:** Same pattern — agent call goes inside a `TaskActivity`.
- **Temporal:** Same — wrap `agent.RunAsync` in an `[Activity]` and call via `Workflow.ExecuteActivityAsync`. Workflow code is replay-deterministic, so LLM calls must be activities. Temporal officially endorses this pattern (OpenAI Agents SDK pairing announced August 2025).

### G. Licensing and cost model

| Engine | License | Cost on Azure (May 2026) |
|---|---|---|
| MAF | MIT | Free |
| Durable Functions | MIT (extension), Azure Functions consumption / Premium / Flex / Dedicated billing | Compute + DTS (or storage account) |
| DTFx | Apache-2.0 | Free + your storage backend |
| Durable Task SDK + DTS | MIT (SDK) + Azure managed service | Consumption SKU: pay-per-action (rate published on Azure Functions pricing page); Dedicated: per-CU/month; plus your ACA compute |
| Temporal Server | MIT | Free; you pay AKS + PostgreSQL + Elasticsearch |
| Temporal Cloud | Commercial SaaS | Dev free; Growth ~$200/mo (1 M actions); Business ~$2 000/mo; ~$0.00025/action above |

### H. Known limitations, anti-patterns, and gotchas

- **MAF supersteps:** synchronization barrier means a fan-out where one branch is slow blocks all others; Diagrid's March 2026 analysis confirmed superstep-level (not activity-level) checkpointing, no duplicate-execution prevention, and no built-in fencing. Treat MAF's in-proc durability as **resumability**, not durable execution. Resume is exact; if you change the graph, the graph-signature hash forces refusal to resume — so plan a drain-and-discard migration before deploying graph changes.
- **Durable Functions:** orchestrator code must be deterministic (no `DateTime.Now`, no GUIDs, no I/O). Using `ConfigureAwait(false)` inside an orchestrator can break replay. In-process model EOL 10 Nov 2026. Functions runtime v1 EOL 14 Sept 2026. On Consumption plan, **every replay is a billable invocation**.
- **DTFx self-hosted:** Microsoft has explicitly stopped recommending it for new projects. No support tickets. Limited modern integrations (no native Entra ID auth on most providers, no built-in dashboard equivalent to DTS).
- **DT SDK + DTS:** versioning is via string compare (`UseDefaultVersion("1.0.0")`), with `Reject` (default), `Fail`, or strict strategies — plan workflow versioning before first deploy. Consumption SKU caps at 500 actions/sec and 30-day retention; Dedicated needs ≥3 CUs (~$$$ in lower environments) for HA.
- **Temporal:** workflow event history hard limits at 50 000 events / 50 MB per run — must use `ContinueAsNew` for long-running entity workflows. Workflows must run on Temporal's deterministic `TaskScheduler.Current`; using `Task.Run` or `ConfigureAwait(false)` will fail the workflow task. Native Rust C-bridge means RID-specific images (musl vs glibc). On Azure Flexible Server PostgreSQL: TLS-by-default and missing `BTREE_GIN` extension are the most common pitfalls; archival to Azure Blob Storage is **not supported** (only S3/GCS) — plan an alternate retention strategy for compliance.

### I. Hybrid pattern recommendation

The cleanest architecture for the bank's stated requirements:

1. **Mission lifecycle and HITL outer workflow** runs on the **Durable Task SDK + Durable Task Scheduler** in an ASP.NET Core Container App (one Aspire project). This gives:
 - GA, Microsoft-supported durability that survives pod recycle.
 - `WaitForExternalEvent<T>(timeout)` for the 4-hour approval / 24-hour escalation pattern.
 - Per-activity retry policies for the 10-minute step timeout.
 - DTS dashboard with Entra ID + RBAC for the auditability requirement.
 - Built-in KEDA scaler in ACA for the per-mission concurrency cap.
 - DTS scheduling (preview, .NET) or a simple cron container for nightly scans (until DTS scheduling is GA across SDKs).
2. **Per-mission agent graph** is a MAF `WorkflowBuilder` invoked **inside a Durable Task activity** as a child orchestration. Today (May 2026), wrap the MAF run with the in-proc runner because `Microsoft.Agents.AI.DurableTask` is still preview (1.4.0-preview.260505.1). When that extension reaches GA, swap the host to it without changing the workflow definition.
3. **Budget enforcement** lives as a workflow middleware that runs at every superstep boundary (MAF) and as a pre-activity hook in the outer orchestrator (DT SDK).
4. **Observability**: MAF emits OpenTelemetry spans for every executor; DT SDK emits OTel + DTS dashboard records every state transition; Aspire ServiceDefaults forwards both to Application Insights / Log Analytics for the regulated audit trail.

If the bank already operates Kubernetes + PostgreSQL at production grade and wants the strongest pause/resume primitives (especially Temporal **Updates** for synchronous validated approvals), Temporal on AKS with a worker container in ACA is the alternative — but cost and operational complexity step up materially.

### J. When to choose each option (decision summary)

| Choose… | When |
|---|---|
| **MAF in-proc only** | POCs, single-pod jobs, where loss of in-flight state on a deploy is acceptable. **Not** for regulated production. |
| **Durable Functions (.NET isolated)** | You're already standardised on Azure Functions and the team accepts the Functions host model; you don't need to embed in an ASP.NET Core / Aspire app. |
| **DTFx self-hosted** | Existing legacy DTFx codebase. Avoid for greenfield (no Microsoft support). |
| **Durable Task SDK + DTS** *(default for the bank)* | Greenfield .NET, ACA + Aspire, regulated workload, want Microsoft-supported managed durability with embedded ASP.NET Core hosting. |
| **Temporal self-hosted on AKS** | You need Temporal-only features (Updates, advanced versioning, multi-cluster replication) and have AKS + Postgres ops capacity, including a workaround for Azure-blob archival and BTREE_GIN. |
| **Temporal Cloud** | Same feature set as above, no ops burden, but data-residency + third-party SaaS compliance review required and cost floor ≈$200/month. |

---

## Caveats

- **MAF Durable Task extension is still preview** (1.4.0-preview.260505.1, last published 5 May 2026). The promise — "same `WorkflowBuilder`, durable host" — is real and Microsoft-built, but the API surface may shift before GA. For production today, treat MAF as the *agent graph* and Durable Task SDK as the *durable mission engine*; switch to fully-MAF-durable when GA lands.
- **DTS scheduling for cron** is currently in the .NET SDK only and listed as preview in Microsoft's announcements; for other languages or for a fully-supported scheduler today, fall back to an external trigger (Azure Logic Apps, ACA cron job, or Temporal Schedules).
- **Activity time caps:** Durable Functions activity time is bounded by the underlying Functions host plan (≤5 min Consumption, configurable up to unlimited on Premium/Flex). Durable Task SDK on ACA has no such cap because you own the worker process — important if any agent call legitimately needs >10 minutes.
- **Source quality:** the architectural assessment of MAF's durability gaps (no fencing, superstep-level granularity, two-process resume) draws on Diagrid's March 2026 critique; their bias should be acknowledged (they sell Dapr-based competing tooling), but the technical claims line up with Microsoft's own discussion thread (`agent-framework#1092`) where the team confirms checkpointing is not a substitute for durable execution and points to Durable Task as the production answer.
- **Pricing figures** for DTS Consumption and Temporal Cloud are summarized from announcement blogs and third-party trackers (Automation Atlas March 2026); validate against the live `azure.microsoft.com/pricing/details/functions` and `temporal.io/pricing` pages before committing.
- **Microsoft support contracts** explicitly cover Durable Functions, Durable Task SDKs + DTS, and MAF (GA core). They do **not** cover DTFx self-hosted or Temporal — for a regulated bank this is often a non-negotiable filter and pushes the recommendation firmly toward Durable Task SDK + DTS.
- This report does not evaluate Dapr Workflows (CNCF graduated, also based on Durable Task) — worth a separate evaluation if the bank is multi-cloud or already operates Dapr on ACA.