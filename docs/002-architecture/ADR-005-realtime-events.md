# ADR-005: Real-time Event Streaming

**Status:** Accepted
**Date:** 2026-05-10
**Decision Makers:** Architecture team
**Research:** [R05-response-realtime-events.md](../098-research/R05-response-realtime-events.md)

---

## Context

The Agentic Workforce Platform streams real-time events (agent execution, project state, console output, notifications, approval requests) to multiple surfaces: React SPA (browser), CLI (terminal), TUI, Telegram, and external API consumers. The CLI cannot use WebSocket — it needs SSE. Scale is 5-20 concurrent consumers, 100-500 events/sec bursts.

## Decision

**Dual approach: Self-hosted SignalR + Redis backplane for channels, plus plain SSE for ephemeral agent streams**

### Primary: Self-hosted SignalR with Redis backplane

For persistent multi-listener channels (project events, session events, user notifications):

```csharp
// Hub definition
public sealed class ProjectHub : Hub
{
    public async Task JoinProject(string projectId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");

    public async Task JoinSession(string sessionId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
}

// Publishing from MAF workflow event loop
public sealed class AgentRunner(IHubContext<ProjectHub> hub)
{
    public async Task RunAsync(string projectId, string sessionId, ...)
    {
        var run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await foreach (var evt in run.WatchStreamAsync())
            await hub.Clients.Group($"project:{projectId}").SendAsync("WorkflowEvent", Map(evt));
    }
}
```

- SignalR auto-negotiates WebSocket → SSE → LongPolling (CLI gets SSE automatically)
- Groups keyed on `project:{id}`, `session:{id}`, `user:{id}` for per-channel pub/sub
- Redis backplane for multi-replica Container Apps deployment
- JWT Bearer auth with query-string fallback for SSE/browser transports

### Secondary: Plain SSE for ephemeral per-run streams

For single-shot agent response streams (one prompt → many tokens → done):

```csharp
app.MapGet("/api/v1/streams/runs/{sessionId}",
    (string sessionId, AgentRunner runner, CancellationToken ct) =>
        TypedResults.ServerSentEvents(runner.StreamAsync(sessionId, ct), eventType: "agent"));
```

- Uses ASP.NET Core 10's `Results.ServerSentEvents` over `IAsyncEnumerable<SseItem<T>>`
- No SignalR overhead for single-consumer ephemeral streams
- Matches Microsoft's AG-UI pattern (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`)
- `EventId` enables reconnect-with-resume via `Last-Event-ID` header

### Channel topology

| Channel | Pattern | Key |
|---------|---------|-----|
| Project events (all sessions) | SignalR group | `project:{projectId}` |
| Session events (filtered) | SignalR group | `session:{sessionId}` |
| User notifications | SignalR group | `user:{userId}` |
| Inline agent response | Plain SSE endpoint | Per-request `IAsyncEnumerable` |
| Inline chat message response | Plain SSE endpoint | Per-request `IAsyncEnumerable` |

## Alternatives Considered

| Option | Verdict | Why Not |
|--------|---------|---------|
| Azure SignalR Service | Rejected at this scale | Free tier caps at 20 connections (server SDK uses 5); Standard_S1 at ~$49/mo is overkill for 5-20 consumers |
| Azure Web PubSub | Eliminated | No SSE/long-polling fallback — WebSocket only on client side; CLI consumers can't use it |
| Pure SSE only (no SignalR) | Available as simpler alternative | Loses bidirectional client→server RPC and auto-reconnect sugar; acceptable if we don't need hub methods |
| Azure SignalR Service (future) | Reconsider at scale | When consumers grow to hundreds+, Azure SignalR Service eliminates sticky sessions and Redis backplane management |

## Container Apps Configuration

```bicep
// Sticky sessions required for self-hosted SignalR negotiate
properties: {
  configuration: {
    ingress: {
      stickySessions: {
        affinity: 'sticky'
      }
    }
  }
}
```

- Single-revision mode + HTTP ingress with sticky sessions
- Envoy keeps-alive SSE/WebSocket streams as long as bytes flow
- Emit `: ping\n\n` heartbeat every 30-60s for disconnect detection

## Aspire Integration

```csharp
// AppHost
var redis = builder.AddRedis("events");
var api = builder.AddProject<AgenticWorkforce_BFF>("bff")
    .WithReference(redis);

// BFF Program.cs
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("events")!);
```

## Cost

- Redis Basic C0: ~$16/mo (or run as Container App sidecar)
- No Azure SignalR Service cost
- No Web PubSub cost
- Total real-time infrastructure: ~$16/mo

## Consequences

- Sticky sessions required in Container Apps (single-revision mode) — limits scaling flexibility but fine at 5-20 consumers
- Redis is a dependency for both events and other concerns (rate limits, idempotency cache) — already in the stack
- SignalR does NOT buffer messages when Redis is down — design for event loss tolerance (UI reconnects and catches up from DB)
- AG-UI integration (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`) available as a reference pattern for SSE endpoints
- MAF `RequestInfoEvent` (HITL) flows through unchanged to the UI for approval rendering

### Principle Compliance

- **P15 Backend Owns All Logic:** Event routing, filtering, and group membership are computed server-side. Clients subscribe to channels — they do not select which events to receive at field level. Event transformation is backend-side before publishing.
- **P16 Single Source of Truth:** PostgreSQL event store is the authoritative record. SignalR/Redis is a transient delivery mechanism. On client reconnection, catch-up is from the database (source of truth), not from Redis replay.
- **P17 Human Authority:** Kill switch events propagate immediately through all channels, overriding in-progress agent streams. Humans can mute notification channels per-project without affecting event persistence.
- **P18 Idempotency:** Events carry a unique `EventId` (UUIDv7). Clients handle duplicate events gracefully on reconnection. The event persistence layer uses `EventId` as a natural deduplication key.
- **P20 Version Everything:** Event payloads include a `schema_version` field. SignalR hub method names and SSE event types are part of the API contract — not renamed without a versioned migration path.
- **P21 Explicit Over Implicit:** SignalR group membership requires explicit `JoinProject`/`JoinSession` calls — clients are never auto-subscribed. All event types are enumerated in a shared contract — no dynamic/arbitrary strings.
