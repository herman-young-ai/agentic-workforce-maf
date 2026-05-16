# Real-Time Event Streaming Options for an AI Agent Orchestrator on Azure Container Apps (May 2026)

## TL;DR

- **For an internal AI orchestrator at 5–20 concurrent consumers / 100–500 events/sec, self-hosted ASP.NET Core SignalR with a Redis backplane (or even no backplane if you stay in single-revision mode) is the cheapest, simplest, and most flexible choice.** Azure SignalR Service is technically a great fit and supports SSE fallback, but its lowest paid tier (Standard_S1, ~$49/unit/month) is hard to justify at this scale, and the Free tier's 20-connection cap is right on the edge of your requirement.
- **For a CLI consumer that cannot use WebSocket, SSE works on every option** — Azure SignalR Service, Web PubSub (with caveats), and self-hosted SignalR all expose SSE through the standard SignalR transport negotiation; ASP.NET Core 10's native `Results.ServerSentEvents` (over `IAsyncEnumerable<T>`) is the cleanest path if you want a plain SSE endpoint outside the SignalR hub abstraction.
- **For Microsoft Agent Framework (MAF, formerly AutoGen) workflow events**, the recommended pattern is to consume `run.WatchStreamAsync()` (which yields `WorkflowEvent` objects like `ExecutorInvokedEvent`, `AgentResponseUpdateEvent`, `WorkflowOutputEvent`) and either (a) fan out into a SignalR hub via `IHubContext<T>.Clients.Group(...).SendAsync(...)`, or (b) yield events as SSE via `Results.ServerSentEvents(...)`. Microsoft also ships an official **AG-UI** ASP.NET Core integration (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`) that already exposes MAF agent runs as SSE.

---

## Key Findings

### 1. Azure SignalR Service supports SSE fallback — but with operational caveats

The `/negotiate` response from Azure SignalR Service explicitly advertises three transports:

```json
"availableTransports": [
  { "transport": "WebSockets",        "transferFormats": ["Text", "Binary"] },
  { "transport": "ServerSentEvents",  "transferFormats": ["Text"] },
  { "transport": "LongPolling",       "transferFormats": ["Text", "Binary"] }
]
```

The standard SignalR transport-negotiation algorithm prioritizes WebSockets and falls back to SSE, then long polling. Important caveat from Microsoft's own engineering team (GitHub issue Azure/azure-signalr#1325): some corporate proxies silently swallow SSE traffic, and the service applies a heuristic — it disables SSE for an IP that fails handshake three times in 30 minutes. For a CLI consumer using `HttpClient` directly (not behind a corporate proxy), this is a non-issue; for browser CLIs running behind unknown corporate networks, you want to test fallback behavior. Microsoft's own deployment behind API Management is documented as supporting the WS↔SSE↔LongPolling fallback chain only when the proxy is configured per their SignalR APIM guide.

### 2. SignalR streams `IAsyncEnumerable<T>` natively from a hub method

A hub method automatically becomes a streaming hub method when it returns `IAsyncEnumerable<T>`, `ChannelReader<T>`, `Task<IAsyncEnumerable<T>>`, or `Task<ChannelReader<T>>`. The async-iterator form (with `[EnumeratorCancellation] CancellationToken`) is the simplest:

```csharp
public async IAsyncEnumerable<AgentEvent> RunAgent(
    string prompt,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var evt in _orchestrator.RunStreamingAsync(prompt, ct))
        yield return Map(evt);
}
```

Clients call `connection.StreamAsync<AgentEvent>("RunAgent", prompt, cts.Token)` and receive an `IAsyncEnumerable<AgentEvent>`. The cancellation token is wired end-to-end: when the client disposes/cancels, the server-side token fires. This pattern is the **canonical way to expose ephemeral inline agent streams** without creating a per-session group/channel.

In .NET 9, SignalR adds Native AOT and trimming support; the only relevant restriction is that `IAsyncEnumerable<T>`/`ChannelReader<T>` parameters cannot be value types (`struct`) for AOT, and strongly-typed hubs are not yet AOT-compatible. None of this matters for a JIT-compiled Container App image.

### 3. Recommended pattern for publishing MAF workflow events

Microsoft Agent Framework 1.0 (GA April 2026) — the unification of AutoGen + Semantic Kernel — exposes a uniform event stream via `InProcessExecution.RunStreamingAsync(...).WatchStreamAsync()`. Built-in event types include `WorkflowStartedEvent`, `WorkflowOutputEvent`, `WorkflowErrorEvent`, `ExecutorInvokedEvent`, `ExecutorCompletedEvent`, `ExecutorFailedEvent`, `AgentResponseEvent`, `AgentResponseUpdateEvent` (the streaming-token event), `SuperStepStartedEvent`, `SuperStepCompletedEvent`, and `RequestInfoEvent` (human-in-the-loop). Custom events can be emitted via `IWorkflowContext.AddEventAsync(new MyEvent(...))`.

The two recommended publishing patterns:

**(a) SignalR hub-context fan-out (per-channel topology — recommended for your case):**

```csharp
public sealed class AgentRunner(IHubContext<AgentHub> hub) {
  public async Task RunAsync(string missionId, string sessionId, ...)
  {
    var run = await InProcessExecution.RunStreamingAsync(workflow, input);
    var group = $"mission:{missionId}:session:{sessionId}";
    await foreach (var evt in run.WatchStreamAsync())
        await hub.Clients.Group(group).SendAsync("WorkflowEvent", Map(evt));
  }
}
```

Consumers join groups via `Groups.AddToGroupAsync(...)` keyed on mission/session/user, giving you the per-channel pub/sub topology you asked about. Group membership is per-connection and must be re-established on reconnect (this is the well-known SignalR scale-out wrinkle).

**(b) Native SSE endpoint (best when MAF events power a single ephemeral stream rather than a long-lived channel):**

```csharp
app.MapGet("/missions/{id}/stream", (string id, AgentRunner r, CancellationToken ct) =>
    TypedResults.ServerSentEvents(r.StreamAsync(id, ct), eventType: "agent"));
```

`TypedResults.ServerSentEvents` (ASP.NET Core 10) takes an `IAsyncEnumerable<SseItem<T>>` and lets you set `EventId`, which together with the `Last-Event-ID` request header gives you reconnect-with-resume semantics. This is the pattern used by Microsoft's own **AG-UI** integration (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`), which calls `MapAGUI("/", agent)` and bridges Agent Framework events directly to AG-UI SSE protocol events — already plugged into CopilotKit and ChatKit.

### 4. Cost analysis at 5–20 consumers, 100–500 events/sec bursts

| Service                  | Lowest paid tier                       | Free tier                               | Verdict for 5–20 consumers                              |
|--------------------------|----------------------------------------|-----------------------------------------|---------------------------------------------------------|
| **Azure SignalR Service**| Standard_S1 ≈ **$1.61/unit/day** ≈ **$49/mo** per unit (1,000 conns, 1M msgs/day free) | Free_F1: 20 concurrent conns, 20K msgs/day | Free tier's 20-conn cap is *exactly* your upper bound and counts both client + server connections (server SDK opens 5 per hub). You'll pop the cap. → Standard_S1 ≈ $50/mo. |
| **Azure Web PubSub**     | Standard_S1: similar per-unit pricing (1K conns/unit) | Free_F1: 20 conns, 20K msgs/day        | Same shape as SignalR Service; protocol-agnostic but no `IAsyncEnumerable` streaming sugar. |
| **Self-hosted SignalR (in-memory)** | $0 incremental                | n/a                                     | Free if single replica; breaks across replicas without a backplane. |
| **Self-hosted SignalR + Redis backplane** | Azure Cache for Redis Basic C0 ≈ **$16/mo**; or run Redis as a Container App sidecar | n/a                            | **Cheapest viable production option.** Redis is also useful for other things (rate limits, distributed locks). |
| **Plain SSE (`Results.ServerSentEvents`)** | $0 incremental                | n/a                                     | Cheapest; pairs well with Channels for per-channel pub/sub in-memory. |

**Verdict:** at 5–20 concurrent consumers, neither Azure SignalR Service nor Web PubSub is cost-justified on connection count. The reason to pay for them is offload (so app servers can scale on CPU/message rate independently of socket count) and the no-sticky-sessions property — neither of which is operationally meaningful at this scale.

---

## Details — Per-Option Assessment

### Option 1 — Plain ASP.NET Core SSE (`IAsyncEnumerable<T>` + Redis pub/sub or in-memory `Channel<T>`)

| Criterion | Assessment |
|---|---|
| WebSocket | ❌ SSE only (uni-directional). Pair with a separate `POST` endpoint for client-to-server. |
| SSE fallback | ✅ It *is* SSE. Native browser/CLI support via `EventSource` and HTTP/1.1+keep-alive. |
| Per-channel pub/sub | Manual: in-memory via `ConcurrentDictionary<string, Channel<T>>` keyed on mission/session/user, or Redis pub/sub for multi-replica. |
| Auth | Standard ASP.NET Core JWT Bearer middleware on the endpoint. Browsers can't set headers on `EventSource`, so use `?access_token=...` and rewire `JwtBearerEvents.OnMessageReceived` to read the query string (same pattern documented for SignalR). Short-lived SSE-specific tokens are easy to mint. |
| Backpressure | First-class via `Channel.CreateBounded<T>(new BoundedChannelOptions(N) { FullMode = DropOldest })`. You own the policy. |
| Auto-reconnect | Native: browsers reconnect automatically and send `Last-Event-ID`; you set `EventId` on each `SseItem<T>` to enable resume. .NET `HttpClient` + `Sse.Item` parser in .NET 9+ supports the same. |
| Container Apps fit | ✅ Excellent. SSE is plain HTTP/1.1, traverses Envoy fine. Envoy's 240 s default request timeout is *not* applied to streaming responses as long as bytes flow; emit a comment-line heartbeat (`: ping\n\n`) every 30–60 s. |
| Sticky sessions | Not needed if events come from a Redis pub/sub fanout; needed only if you keep state in-replica `Channel`s. |
| Managed vs self-hosted | Self-hosted; everything is your code. |
| Cost (5–20) | $0 incremental (Redis ~$16/mo if you need cross-replica pub/sub). |
| Implementation complexity | **Lowest.** ~50 lines for the endpoint + a publisher class. |
| .NET Aspire | ✅ Trivial — Redis hosted/container-ized via `builder.AddRedis("events")` and consumed with `Aspire.StackExchange.Redis`. |
| MAF integration | ✅ Native — `IAsyncEnumerable<WorkflowEvent>` straight into `Results.ServerSentEvents`. Microsoft's AG-UI package already uses this exact pattern. |

### Option 2 — Azure SignalR Service (Default mode)

| Criterion | Assessment |
|---|---|
| WebSocket | ✅ Primary transport. |
| SSE fallback | ✅ Advertised in `/negotiate`; selected automatically when WS fails. Caveat: if 3 SSE handshakes fail from one IP within 30 min, the service disables SSE for that IP. |
| Per-channel pub/sub | ✅ Groups (`Groups.AddToGroupAsync`) keyed per mission/session; users via `Clients.User(...)`. Server-sticky mode `Required` if your hub needs in-process state (e.g., for streaming a single connection). |
| Auth | JWT Bearer flows through transparently. Service issues short-lived access tokens on each `/negotiate` (default 1 hour). Claims passed via `ClaimsProvider`. |
| Backpressure | Limited control — service-side buffering with disconnect on slow consumer. Outbound message rate is metered (>2 KB messages count as multiple). |
| Auto-reconnect | ✅ `WithAutomaticReconnect()` (defaults: 0, 2, 10, 30 s). Service issues 401 when token expires → client must reconnect to negotiate fresh token. |
| Container Apps fit | ✅ Best-in-class. Connections are offloaded to the service, so your Container App scales on HTTP/CPU only — **no sticky sessions required** even in multi-replica revisions. |
| Managed vs self-hosted | Fully managed; Microsoft handles 99.9% (Standard) / 99.95% (Premium) SLA. |
| Cost (5–20) | Free tier blocks at 20 connections (server SDK opens 5 per hub, leaving ~15 for clients). Standard_S1 ≈ **$49/mo** for 1 unit (1K conns, 1M msgs/day, then $1/M extra). |
| Implementation complexity | Low — `services.AddSignalR().AddAzureSignalR()`. Same hub code as self-hosted. |
| .NET Aspire | ✅ Official: `Aspire.Hosting.Azure.SignalR` (current v13.x as of May 2026). Local emulator (`AzureSignalREmulatorResource`) for dev. `AddNamedAzureSignalR()` on the consumer side wires the connection string. |
| MAF integration | Same as Option 4 (use `IHubContext<T>` for fan-out); but you cannot stream `IAsyncEnumerable<T>` from a hub method while running in **Serverless** mode — that requires Default mode (which is what you'd use here). |

### Option 3 — Azure Web PubSub

| Criterion | Assessment |
|---|---|
| WebSocket | ✅ Primary; native WebSocket — no SignalR-specific protocol required. |
| SSE fallback | ⚠️ **Not first-class.** Web PubSub's client protocol is WebSocket-native (with optional `json.webpubsub.azure.v1` subprotocol). Server-Sent Events and long polling are *not* part of the documented client transports the way they are with SignalR. CLI consumers must speak WebSocket — this is a deal-breaker for "CLI consumers that cannot use WebSocket." |
| Per-channel pub/sub | ✅ Hubs + Groups + UserId — clean three-level addressing model. Clients can `joinGroup(...)` directly with appropriately scoped roles. |
| Auth | Client Access URL with embedded JWT (short-lived); roles encode group/permission scope (`webpubsub.joinLeaveGroup.<group>`). Server uses Microsoft Entra ID or access keys. |
| Backpressure | Service-side buffering; same limitation as SignalR Service. |
| Auto-reconnect | ✅ Built into the official client SDK (`Azure.Messaging.WebPubSub.Client`). |
| Container Apps fit | ✅ Connections offloaded to the service. |
| Managed vs self-hosted | Fully managed; same SKU ladder (Free/Standard/Premium). |
| Cost (5–20) | Free_F1: 20 conns, 20K msgs/day. Standard_S1: similar to SignalR Service per-unit. |
| Implementation complexity | Medium — protocol-agnostic, so you write more glue. CloudEvents-based event handlers if you want push from service to your app. |
| .NET Aspire | Hosting integration exists in the broader Azure ecosystem packages but is less mature than the SignalR one as of May 2026. |
| MAF integration | Possible but no first-class adapter. You'd publish to groups via `WebPubSubServiceClient.SendToGroupAsync(...)` from the MAF event loop. Less natural than SignalR for `IAsyncEnumerable` streams. |

**Conclusion:** Web PubSub is a strong choice for polyglot WebSocket clients, but the **lack of SSE/long-polling fallback for CLI consumers eliminates it given your constraints.**

### Option 4 — Self-hosted SignalR in the Container App + Redis backplane

| Criterion | Assessment |
|---|---|
| WebSocket | ✅ Primary. |
| SSE fallback | ✅ Built into ASP.NET Core SignalR — same `/negotiate` advertises WS, SSE, LongPolling. Identical client-side transport selection as Azure SignalR Service. |
| Per-channel pub/sub | ✅ Groups + Users via `IHubContext<T>`. Redis pub/sub propagates to all replicas. |
| Auth | Standard JWT Bearer + `OnMessageReceived` handler reading `?access_token=...` from query string for browser WS/SSE transports (officially documented pattern). |
| Backpressure | Configurable per-hub: `HubOptions.MaximumParallelInvocationsPerClient`, `StreamBufferCapacity`, plus your own `Channel`-based throttling. |
| Auto-reconnect | ✅ Same `WithAutomaticReconnect()`. |
| Container Apps fit | ⚠️ **Sticky sessions are mandatory** unless all clients use WebSocket-only with `SkipNegotiation=true`. Container Apps GA'd session affinity in Aug 2023 (`stickySessions.affinity = "sticky"`) — works in **single-revision mode with HTTP ingress only**. Cookie-based; clients must accept cookies (most CLI HTTP libs do). |
| Sticky sessions caveat for CLI | The CLI must persist the affinity cookie set by Envoy across the `/negotiate` POST and the subsequent SSE/WS upgrade. `HttpClientHandler { UseCookies = true }` covers this. |
| Managed vs self-hosted | Self-hosted; you own scale-out semantics, message-loss-on-Redis-outage (SignalR does NOT buffer messages when Redis is down), and capacity. |
| Cost (5–20) | **Cheapest production-grade option.** Redis is the only added cost: ~$16/mo Basic C0, or run Redis as another Container App. |
| Implementation complexity | Low–medium. Add NuGet `Microsoft.AspNetCore.SignalR.StackExchangeRedis` and chain `.AddStackExchangeRedis(...)` onto `AddSignalR()`. |
| .NET Aspire | ✅ Excellent: `builder.AddRedis("events")` + reference into the API project; SignalR hub events appear on the Aspire dashboard via .NET 9 OpenTelemetry. |
| MAF integration | Identical to Option 2 (`IHubContext<T>.Clients.Group(...).SendAsync(...)` in your event loop). `IAsyncEnumerable<T>` streaming from hub methods works out of the box. |

**Microsoft's own guidance** (`learn.microsoft.com/aspnet/core/signalr/scale`): "If your SignalR app is running in Azure cloud, we recommend Azure SignalR Service instead of a Redis backplane." That guidance is *biased toward managed convenience* and assumes you're going to scale to thousands of clients. **At 5–20 consumers, the trade-off inverts: self-hosted SignalR + Redis is materially simpler operationally because there is no second Azure resource to authenticate, monitor, and budget.**

---

## Specific Question Answers

**Q1. Can Azure SignalR Service do SSE fallback for CLI consumers? What transport negotiation does it support?**
Yes. The service advertises `WebSockets`, `ServerSentEvents`, and `LongPolling` in its `/negotiate` response. The ASP.NET Core SignalR client automatically selects the highest-priority transport that succeeds (WS → SSE → LP). The service also has an SSE-specific guard: it disables SSE for an IP that fails handshake 3× in 30 minutes (mostly a corporate-proxy issue; not applicable for a clean `HttpClient`-based CLI). One important wart: the .NET SignalR client must NOT have `SkipNegotiation = true` when talking to Azure SignalR Service — the negotiate redirect from your app server to the service URL is mandatory.

**Q2. How does SignalR handle ephemeral inline streams? Can you stream `IAsyncEnumerable` from a hub method?**
Yes. Returning `IAsyncEnumerable<T>` (or `ChannelReader<T>`, or `Task<…>` of either) automatically marks the method as a streaming hub method. The recommended idiom is an async iterator with `[EnumeratorCancellation] CancellationToken`. Clients call `connection.StreamAsync<T>("Method", args, ct)` and receive an `IAsyncEnumerable<T>`. Cancellation propagates server-side. This means a single-shot agent response (one prompt → many tokens → done) does *not* require creating a group/channel; the client invocation itself *is* the channel. Use groups only for true broadcast topologies (mission/session multi-listener).

Note: Streaming-from-hub-method is supported in **Default mode** of Azure SignalR Service but **not in Serverless mode** (which has no persistent server connection — Serverless requires REST API/management SDK and Azure Functions bindings).

**Q3. Recommended pattern for publishing MAF workflow events to a real-time channel?**
Use one of these two patterns, depending on whether the consumer wants a persistent multi-event channel or an ephemeral per-run stream:

- **Per-run ephemeral stream (simplest, lowest moving parts):** Expose either a SignalR streaming hub method or a `MapGet` endpoint returning `Results.ServerSentEvents` over an `IAsyncEnumerable<WorkflowEvent>` produced from `run.WatchStreamAsync()`. Map `ExecutorInvokedEvent`, `AgentResponseUpdateEvent`, `WorkflowOutputEvent`, etc., to your wire DTO. This is exactly how Microsoft's official `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` package (AG-UI integration) ships.
- **Mission/session/user channels (multiple listeners, long-lived):** Run the workflow in a hosted background service; from inside the `await foreach` loop, fan out to a SignalR hub via `IHubContext<AgentHub>.Clients.Group($"mission:{id}").SendAsync("WorkflowEvent", evt, ct)`. Consumers `JoinMission(id)` from the client side, which calls `Groups.AddToGroupAsync(Context.ConnectionId, $"mission:{id}")`.

For both patterns, pass MAF's `RequestInfoEvent` (human-in-the-loop pause) through unchanged so the UI can prompt for approval.

**Q4. Is Azure SignalR Service or Web PubSub justified at 5–20 consumers?**
**No.** At this scale:
- **Web PubSub is eliminated** by the SSE/CLI requirement — it is WebSocket-only on the client side.
- **Azure SignalR Service** Free tier (20 connections, 20K messages/day) is right on the edge of your upper bound and the server SDK consumes ~5 of those connections per hub, leaving ~15 for actual consumers. Standard_S1 at ~$49/month gets you 1,000 connections — vastly overprovisioned.
- **Self-hosted SignalR + Redis backplane** is the recommended choice: cheapest (~$16/mo for Basic C0 Redis, or free if you co-host Redis in the Container Apps environment), simplest to wire up via .NET Aspire (`AddRedis("events")` + `AddStackExchangeRedis(...)` on the hub), and gives you the same `IAsyncEnumerable` streaming, SSE fallback, JWT auth, and auto-reconnect surface.
- **Pure SSE with `Results.ServerSentEvents`** is even simpler if you don't need bidirectional client-to-server invocations — at 5–20 consumers it's quite reasonable to skip the SignalR hub abstraction entirely.

The break-even for Azure SignalR Service is somewhere around several hundred concurrent connections (where managing sticky sessions, Redis health, and per-replica connection counts becomes a real operational burden). You're 1–2 orders of magnitude below that.

---

## Container Apps Specifics (May 2026)

- **WebSocket support:** Native (Envoy ingress proxies WS upgrades). Microsoft Q&A confirms no documented hard limit on concurrent WebSockets per replica beyond what the underlying CPU/memory tier can handle. Envoy's request timeout default is 240 s — irrelevant for streaming as long as bytes flow regularly; emit periodic keep-alive frames or comment-line heartbeats for SSE.
- **SSE support:** Same — plain HTTP/1.1, no special configuration. Application Gateway for Containers (a separate ingress option) explicitly supports SSE only via the Gateway API path (not Ingress API).
- **Sticky sessions / session affinity:** GA since August 2023. Cookie-based, configured via `properties.configuration.ingress.stickySessions.affinity = "sticky"` (Bicep/ARM/Terraform). **Single-revision mode + HTTP ingress only.** Required for self-hosted SignalR with negotiate (so `/negotiate` POST and the subsequent SSE/WS connection land on the same replica).
- **Scaling caveat for raw WebSockets:** KEDA scaling decisions are not instant; you cannot strictly guarantee one WS per replica with `concurrentRequests=1`. For 5–20 consumers this is irrelevant. If you ever need strict 1-WS-per-replica isolation, the Microsoft-recommended pattern is to offload to Azure SignalR Service.
- **Front Door does not support SSE** as of late 2025 (per Microsoft Q&A response). Use Application Gateway for Containers (Gateway API) or expose Container Apps directly if you need SSE through a global entry point.

## .NET Aspire Integration (May 2026)

- `Aspire.Hosting.Azure.SignalR` (latest 13.x) — provisions Azure SignalR Service via Bicep, supports both Default and Serverless modes, includes `AzureSignalREmulatorResource` for local dev (the emulator does not support `AddNamedAzureSignalR()`; use the management SDK package).
- `Aspire.Hosting.Redis` + `Aspire.StackExchange.Redis` — `builder.AddRedis("events")` plus `WithReference(redis)` from your API project; client side `builder.AddRedisClient("events")` registers `IConnectionMultiplexer`. Health checks, OpenTelemetry tracing, and the Aspire dashboard Redis viewer (DbGate via Community Toolkit) are wired automatically.
- **There is no official Aspire client integration for SignalR clients themselves** — the `dotnet/aspire` issue (#799) was closed by adding hosting integration only, on the rationale that SignalR clients live in separate projects and wire up via the connection string convention.
- SignalR hub events flow into the Aspire dashboard via .NET 9's built-in OpenTelemetry instrumentation — useful for debugging at dev time.

## MAF / Microsoft.Extensions.AI Workflow Event Integration

- MAF 1.0 (April 2026) is the unification of AutoGen + Semantic Kernel; events are emitted from `WorkflowEvent` (subtypes: `ExecutorInvokedEvent`, `ExecutorCompletedEvent`, `AgentResponseUpdateEvent` for streaming tokens, `WorkflowOutputEvent`, `RequestInfoEvent` for HITL, plus user-defined custom events).
- **Custom events** are emitted via `IWorkflowContext.AddEventAsync(new MyEvent(...))` from within an executor — useful for surfacing tool-call progress to the UI.
- **Streaming-as-agent pitfall** (open MAF discussion #1474, Oct 2025): when you wrap a workflow as `AsAgentAsync()` and call `RunStreamingAsync` on the wrapped agent, internal agents may stream directly to the caller rather than the workflow producing a single funneled stream. Consume `WatchStreamAsync()` directly from the workflow run if you want a single, controlled fan-out point.
- **Microsoft's AG-UI ASP.NET Core integration** (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`) is the reference end-to-end pattern: HTTP POST + SSE, middleware pipeline, automatic conversion of MAF events to AG-UI protocol events, plug-and-play with CopilotKit/ChatKit. This is essentially Option 1 (plain SSE) with a Microsoft-maintained protocol adapter on top.

---

## Final Recommendation

For a 5–20-consumer internal AI orchestrator on Azure Container Apps with CLI consumers:

1. **Default choice — Self-hosted SignalR + Redis backplane**, deployed via .NET Aspire, with single-revision mode + sticky sessions enabled. Use `IAsyncEnumerable<T>` hub methods for ephemeral per-run streams; use groups (`mission:*`, `session:*`, `user:*`) for multi-listener channels. JWT Bearer with query-string fallback for browser/SSE transports.
2. **Simpler alternative — Pure SSE via `Results.ServerSentEvents`** with in-memory `Channel<T>` (single replica) or Redis pub/sub (multi-replica). Drop SignalR entirely if you don't need client→server RPC. Pairs naturally with MAF's `WatchStreamAsync()` and matches the AG-UI pattern Microsoft itself ships.
3. **Skip Azure SignalR Service / Web PubSub at this scale** — they shine at hundreds-to-millions of connections, not at 5–20. Reconsider if usage grows to several hundred concurrent consumers, or if the operational cost of sticky sessions + Redis becomes significant.

---

## Caveats

- All Azure pricing referenced is from azure.microsoft.com pricing pages and azure.cn mirrors observable in May 2026; specific dollar figures (e.g., "$49/mo for Standard_S1") are widely reported in community blogs and consistent with Microsoft's per-unit/day model, but final billing varies by region and exchange rates — verify with Azure Pricing Calculator for your subscription.
- The Web PubSub assessment that there is no first-class SSE/long-polling client transport is based on Microsoft Learn's Web PubSub client protocol documentation, which only describes WebSocket. If your CLI can speak WebSocket (most modern HTTP clients can), Web PubSub becomes a viable option, but the question explicitly stipulated CLI consumers that cannot.
- The "20 connections" Free-tier limit on Azure SignalR Service / Web PubSub counts both client and server connections (the SDK opens 5 server connections per hub by default). Realistic client headroom on the Free tier is ~15.
- MAF 1.0 GA was announced April 7, 2026 (Microsoft Agent Framework devblogs). Some workflow-streaming behaviors (particularly `AsAgentAsync()` semantics) are still being refined per active GitHub discussions; verify current behavior against the framework version you adopt.
- Azure Container Apps' "WebSocket connection limit per replica" is officially "no documented limit" per Microsoft Q&A but is in practice bounded by the CPU/memory tier you choose; benchmark before committing to a workload that requires hundreds of long-lived sockets per replica.
- The Envoy 240 s request timeout in Container Apps applies to non-streaming HTTP requests; streaming responses (SSE, chunked) and WebSocket upgrades are not subject to it as long as data flows. Implement application-level keep-alive heartbeats anyway to detect half-open connections quickly.
- Microsoft Learn's recommendation to prefer Azure SignalR Service over Redis backplane in Azure assumes scale-out concerns that don't apply at 5–20 consumers; weigh that guidance against your actual concurrency.