# Phase 5: Real-time & Events

**Status:** Not Started
**Depends On:** Phase 4 (API Extended)
**Verification:** Integration test connects to SignalR hub, receives event on project mutation; SSE stream delivers agent tokens

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

Implement the real-time event infrastructure: SignalR hub with Redis backplane for persistent channels, plain SSE for ephemeral agent response streams, `IEventPublisher` producing events to Redis pub/sub, and the project console feed. After this phase, clients receive live updates when project state changes.

---

## Architecture (from ADR-005)

```
Worker/Agent execution
        │ publishes ProjectEvent to Redis pub/sub
        ▼
Redis pub/sub channel: "events:{projectId}"
        │
        ▼
BFF API (SignalR Hub subscriber)
        │ fans out to connected clients
        ▼
React SPA / CLI / TUI (SignalR client or SSE)
```

Two patterns:
1. **SignalR groups** — persistent channels (`project:{id}`, `session:{id}`, `user:{id}`) for multiple listeners, long-lived
2. **Plain SSE** (`Results.ServerSentEvents`) — ephemeral per-run agent response streams (matches AG-UI pattern)

---

## 1. SignalR Hub

### File: `src/AgenticWorkforce.Api/Hubs/ProjectHub.cs`

```csharp
[Authorize]
public class ProjectHub(
    IProjectAuthorizationService authz,
    ISessionRepository sessions) : Hub<IProjectHubClient>
{
    public async Task JoinProject(Guid projectId)
    {
        // BOLA gate: a project group leaks every event for that project, so
        // membership MUST be verified before the SignalR group is joined. A
        // missing check here would let any authenticated client subscribe to
        // any project's stream by guessing IDs.
        var userId = ResolveUserId(Context.User);
        await authz.EnsureRoleAsync(userId, projectId, ProjectRole.Viewer);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");
    }

    public Task LeaveProject(Guid projectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project:{projectId}");

    public async Task JoinSession(Guid sessionId)
    {
        // Sessions are project-scoped — resolve the parent project and check
        // membership there. Sessions have no independent ACL.
        var session = await sessions.GetByIdAsync(sessionId)
            ?? throw new HubException("Session not found.");
        var userId = ResolveUserId(Context.User);
        await authz.EnsureRoleAsync(userId, session.ProjectId, ProjectRole.Viewer);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    public Task LeaveSession(Guid sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");

    public override async Task OnConnectedAsync()
    {
        // Per-user notification group — auto-joined so notifications reach
        // the user regardless of which projects they're actively viewing.
        var userId = ResolveUserId(Context.User);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId:N}");
        await base.OnConnectedAsync();
    }

    private static Guid ResolveUserId(ClaimsPrincipal? principal)
        => Guid.TryParse(principal?.FindFirst("oid")?.Value, out var id) && id != Guid.Empty
            ? id
            : throw new HubException("Token has no valid object-identifier claim.");
}
```

Authorisation failures from `EnsureRoleAsync` (which throws `ForbiddenException`) propagate to the client as a SignalR `HubException` — a structured error, not a silent denied join.

### Client-facing events (sent TO clients):

```csharp
public interface IProjectHubClient
{
    Task ProjectEvent(ProjectEventDto evt);
    Task TaskStatusChanged(TaskStatusChangedDto dto);
    Task SessionMessage(SessionMessageDto dto);
    Task BudgetWarning(BudgetWarningDto dto);
    Task HumanInputRequired(HumanInputRequiredDto dto);
    Task Notification(NotificationDto dto);
}
```

---

## 2. Redis Backplane

### Registration in `Program.cs`:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis")!, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("agentic:");
    });

app.MapHub<ProjectHub>("/hubs/project");
```

### Package additions to Api.csproj:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
```

---

## 3. IEventPublisher Implementation (No Circular Dependency)

The architecture requires: Worker publishes to Redis → Api subscribes and fans out via SignalR. This avoids Infrastructure depending on Api's hub types.

**Two-part design:**

1. `IEventPublisher` (in Infrastructure) writes to DB + Redis pub/sub. No SignalR knowledge.
2. `SignalREventRelay` (in Api) subscribes to Redis pub/sub and pushes to SignalR groups.

### File: `src/AgenticWorkforce.Infrastructure/Events/RedisEventPublisher.cs`

```csharp
internal sealed class RedisEventPublisher(
    IRedisPubSubService redisPubSub,
    AppDbContext db,
    ILogger<RedisEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync(ProjectEvent evt, CancellationToken ct = default)
    {
        // Durability model (decided here so callers know what to expect):
        //   1. PostgreSQL `project_events` is the durable source of truth —
        //      every event is persisted before this method returns success.
        //   2. Redis pub/sub is a best-effort live-transport for fan-out via
        //      SignalR/SSE. If Redis is unreachable or the publish errors,
        //      we log a warning and return success — the DB row still
        //      represents the event. Clients reconcile by re-fetching the
        //      events feed with a `since=...` cursor on reconnect.
        //
        // This avoids the phantom failure mode where DB commit succeeded but
        // the caller (e.g. an endpoint handler) sees an exception and rolls
        // back its own response — recording the event twice when retried.

        db.ProjectEvents.Add(evt);
        await db.SaveChangesAsync(ct);

        try
        {
            var payload = JsonSerializer.Serialize(MapToDto(evt));
            await redisPubSub.PublishAsync($"events:{evt.ProjectId}", payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Redis pub/sub failed for {EventType} on project {ProjectId}; "
                + "event persisted, clients will replay via the events feed",
                evt.EventType, evt.ProjectId);
        }

        logger.LogInformation(
            "Persisted {EventType} for project {ProjectId}",
            evt.EventType, evt.ProjectId);
    }

    public async Task PublishAsync(string channel, string eventType, object data, CancellationToken ct = default)
    {
        // The string-channel overload is for transient signals (UI hints,
        // heartbeats) that have no DB counterpart. Same best-effort model.
        var payload = JsonSerializer.Serialize(new { eventType, data, timestamp = DateTime.UtcNow });
        try
        {
            await redisPubSub.PublishAsync(channel, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Redis pub/sub failed for transient {EventType} on channel {Channel}",
                eventType, channel);
        }
    }
}
```

### File: `src/AgenticWorkforce.Api/Services/SignalREventRelay.cs`

Background service in Api that subscribes to Redis and fans out via SignalR:

```csharp
internal sealed class SignalREventRelay(
    IRedisPubSubService redisPubSub,
    IHubContext<ProjectHub, IProjectHubClient> hubContext,
    ILogger<SignalREventRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // One bad message must not kill the relay. Catch per-iteration so a
        // malformed payload or a transient hub-send failure is logged and
        // the loop continues consuming subsequent messages.
        await foreach (var (channel, message) in redisPubSub.SubscribePatternAsync("events:*", ct))
        {
            try
            {
                var projectId = channel.Replace("events:", "", StringComparison.Ordinal);
                var evt = JsonSerializer.Deserialize<ProjectEventDto>(message);
                if (evt is null)
                {
                    logger.LogWarning("Dropping malformed event from {Channel}", channel);
                    continue;
                }
                await hubContext.Clients
                    .Group($"project:{projectId}")
                    .ProjectEvent(evt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to relay event from {Channel}; payload preserved in project_events",
                    channel);
            }
        }
    }
}
```

This keeps Infrastructure free of any Api/SignalR references. The relay is registered in Api's `Program.cs` as a hosted service.

### Publish from endpoints:

Inject `IEventPublisher` (from Infrastructure) into endpoints — same usage as before:

```csharp
// Example: ApproveTask.cs publishes event after approval
await eventPublisher.PublishAsync(new ProjectEvent
{
    ProjectId = task.ProjectId,
    TaskId = task.Id,
    EventType = "task.approved",
    Source = user.Email,
    Severity = EventSeverity.Info,
    Data = JsonSerializer.Serialize(new { task.Id, task.Objective, ApprovedBy = user.Email })
}, ct);
```

---

## 4. SSE Endpoints (Agent Response Streaming)

### File: `src/AgenticWorkforce.Api/Features/Events/StreamProjectEvents.cs`

Stream endpoints all share three concerns: enforce viewer-or-higher
membership before opening the stream, set SSE headers including the
buffering-disable hint, and keep the connection alive past idle proxies via
a periodic `: ping` comment (CDNs/load balancers commonly drop idle
connections at 30–60s).

```csharp
public static class StreamProjectEvents
{
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/projects/{projectId:guid}/events/stream", HandleAsync)
            .RequireAuthorization("SseStream")   // accepts JWT or SSE token (see §5)
            .WithTags("Events");
    }

    private static async Task HandleAsync(
        Guid projectId,
        HttpContext httpContext,
        IProjectAuthorizationService authz,
        ICurrentUserAccessor userAccessor,
        IRedisPubSubService redisPubSub,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        httpContext.Response.Headers.ContentType  = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection   = "keep-alive";
        // Hint to Nginx/Azure Front Door to not buffer the response so the
        // first event reaches the client immediately.
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        // 15s heartbeat: a `:`-prefixed line is a valid SSE comment per the
        // spec, so the client never surfaces it to application code — it
        // exists purely to keep middleboxes from dropping the connection.
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (await heartbeat.WaitForNextTickAsync(ct))
                {
                    await httpContext.Response.WriteAsync(": ping\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnect */ }
        }, ct);

        try
        {
            await foreach (var msg in redisPubSub.SubscribeAsync($"events:{projectId}", ct))
            {
                var evt = JsonSerializer.Deserialize<ProjectEventDto>(msg);
                if (evt is null) continue;
                await httpContext.Response.WriteAsync(
                    $"event: {evt.EventType}\ndata: {msg}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        }
        finally
        {
            heartbeat.Dispose();
            try { await heartbeatTask; } catch (OperationCanceledException) { }
        }
    }
}
```

### File: `src/AgenticWorkforce.Api/Features/Events/StreamTaskEvents.cs`

SSE scoped to a single task execution — used by the UI to show live agent
output. Same auth/header/heartbeat scaffold; channel is `events:{projectId}`
filtered server-side to `TaskId == taskId`.

```csharp
app.MapGet("/api/v1/projects/{projectId:guid}/events/stream/tasks/{taskId:guid}", HandleAsync)
    .RequireAuthorization("SseStream")
    .WithTags("Events");
```

### File: `src/AgenticWorkforce.Api/Features/Events/StreamNotifications.cs`

Per-user notification stream (all projects). Subscribes to
`user:{userId:N}:notifications` rather than a project channel — there's no
project-id parameter, so authorisation is "the connected user is the user
this stream is for", not project membership.

```csharp
app.MapGet("/api/v1/notifications/stream", HandleAsync)
    .RequireAuthorization("SseStream")
    .WithTags("Notifications");
```

---

## 5. SSE Token Exchange

The browser `EventSource` API can't set request headers, so the client
exchanges its JWT for a short-lived single-use token and appends it to the
SSE URL as `?token=…`. The token is stored in Redis with a 30 s TTL; the
auth handler reads it via `GETDEL` so a token can be redeemed exactly once.

### File: `src/AgenticWorkforce.Api/Features/Auth/CreateSseToken.cs`

```csharp
public static class CreateSseToken
{
    public record Response(string Token, int ExpiresInSeconds);

    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/sse-token", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth");
    }

    private static async Task<IResult> HandleAsync(
        ICurrentUserAccessor userAccessor,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        // 256-bit cryptographically-random token rather than a v4 Guid so
        // brute-force across the 30s TTL window is infeasible.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        // Snapshot the claims the SSE handler will reconstruct the principal
        // from. This IS the authorisation truth the stream endpoint sees —
        // include Roles for downstream policy checks.
        var snapshot = JsonSerializer.Serialize(new
        {
            user.Id,
            user.Email,
            user.Roles
        });

        var db = redis.GetDatabase();
        await db.StringSetAsync($"sse-token:{token}", snapshot, TimeSpan.FromSeconds(30));

        return Results.Ok(new Response(token, 30));
    }
}
```

### File: `src/AgenticWorkforce.Api/Core/Auth/SseTokenAuthHandler.cs`

```csharp
public sealed class SseTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConnectionMultiplexer redis)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SseToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Query.TryGetValue("token", out var tokenRaw)
            || string.IsNullOrWhiteSpace(tokenRaw))
            return AuthenticateResult.NoResult();  // let other schemes try

        var db = redis.GetDatabase();
        // Atomic single-use: GETDEL returns the value AND removes the key in
        // one round-trip, so a replay of the same `?token=` value returns
        // null on the second attempt.
        var snapshotJson = await db.StringGetDeleteAsync($"sse-token:{tokenRaw}");
        if (!snapshotJson.HasValue)
            return AuthenticateResult.Fail("Invalid or already-redeemed SSE token.");

        var snapshot = JsonSerializer.Deserialize<SseTokenSnapshot>(snapshotJson!);
        if (snapshot is null)
            return AuthenticateResult.Fail("Malformed SSE token payload.");

        var claims = new List<Claim>
        {
            new("oid",                       snapshot.Id.ToString()),
            new(ClaimTypes.NameIdentifier,   snapshot.Id.ToString()),
            new(ClaimTypes.Email,            snapshot.Email),
            new("preferred_username",        snapshot.Email),
            new("name",                      snapshot.Email)
        };
        claims.AddRange(snapshot.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private sealed record SseTokenSnapshot(Guid Id, string Email, IReadOnlyList<string> Roles);
}
```

### Scheme + policy registration in `Program.cs`

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .AddScheme<AuthenticationSchemeOptions, SseTokenAuthHandler>(
        SseTokenAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    // ...existing role policies...
    .AddPolicy("SseStream", p => p
        .AddAuthenticationSchemes(
            SseTokenAuthHandler.SchemeName,
            JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
```

The `"SseStream"` policy is the one the stream endpoints apply via
`.RequireAuthorization("SseStream")`. Non-stream endpoints continue to use
the JWT-only default policy — the token scheme is opt-in.

---

## 6. Redis Pub/Sub Service

### File: `src/AgenticWorkforce.Infrastructure/Events/IRedisPubSubService.cs`

```csharp
public interface IRedisPubSubService
{
    Task PublishAsync(string channel, string message, CancellationToken ct = default);

    /// <summary>Subscribe to a single literal channel.</summary>
    IAsyncEnumerable<string> SubscribeAsync(
        string channel, CancellationToken ct = default);

    /// <summary>
    /// Subscribe to a glob-style channel pattern (e.g. <c>"events:*"</c>) so
    /// the consumer sees messages from any matching channel. The yielded
    /// tuple carries the actual channel each message arrived on, since the
    /// consumer typically needs to route by it.
    /// </summary>
    IAsyncEnumerable<(string Channel, string Message)> SubscribePatternAsync(
        string pattern, CancellationToken ct = default);
}
```

### File: `src/AgenticWorkforce.Infrastructure/Events/RedisPubSubService.cs`

Wraps `ISubscriber` for publishing and subscribing. Each subscription owns a
bounded in-memory queue: a slow consumer drops the **oldest** buffered
message rather than ballooning memory (Principle 19). The DB-side
`project_events` table is the durable record, so a dropped pub/sub message
is a delayed event, not a lost one.

```csharp
internal sealed class RedisPubSubService(IConnectionMultiplexer redis) : IRedisPubSubService
{
    // Per-subscription buffer. 1k events at ~1KB each = ~1MB upper bound.
    private const int SubscribeBufferSize = 1000;

    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    public IAsyncEnumerable<string> SubscribeAsync(
        string channel, CancellationToken ct = default)
        => SubscribeLiteralAsync(RedisChannel.Literal(channel), ct);

    public IAsyncEnumerable<(string Channel, string Message)> SubscribePatternAsync(
        string pattern, CancellationToken ct = default)
        => SubscribePatternInternalAsync(RedisChannel.Pattern(pattern), ct);

    private async IAsyncEnumerable<string> SubscribeLiteralAsync(
        RedisChannel channel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(SubscribeBufferSize)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        await subscriber.SubscribeAsync(channel, (_, message) =>
        {
            if (message.HasValue) queue.Writer.TryWrite(message!);
        });

        try
        {
            await foreach (var msg in queue.Reader.ReadAllAsync(ct))
                yield return msg;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    private async IAsyncEnumerable<(string Channel, string Message)> SubscribePatternInternalAsync(
        RedisChannel pattern,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(SubscribeBufferSize)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        await subscriber.SubscribeAsync(pattern, (channel, message) =>
        {
            if (message.HasValue) queue.Writer.TryWrite((channel.ToString(), message!));
        });

        try
        {
            await foreach (var msg in queue.Reader.ReadAllAsync(ct))
                yield return msg;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(pattern);
        }
    }
}
```

---

## 7. Idempotency Service (Redis)

Replace the Phase 3.5 in-memory stub with a Redis-backed implementation
that survives Api replica scale-out (an idempotent retry that hits a
different pod must still see the cached response).

**The signature is `(Guid userId, string key, ct)` — do not regress to a
single-key form.** Phase 3.5 added user-scoping to close a cross-user
replay vulnerability: keying only on the header would let user B replay
user A's response (including the `Location` of a created resource) by
sending A's idempotency key. The composed Redis key
`idempotency:{userId:N}:{key}` mirrors the in-memory impl exactly.

### File: `src/AgenticWorkforce.Infrastructure/Services/RedisIdempotencyService.cs`

```csharp
internal sealed class RedisIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<T?> GetCachedResponseAsync<T>(
        Guid userId, string key, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Compose(userId, key));
        return value.HasValue ? JsonSerializer.Deserialize<T>(value!) : default;
    }

    public async Task CacheResponseAsync<T>(
        Guid userId, string key, T response, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(Compose(userId, key), JsonSerializer.Serialize(response), Ttl);
    }

    // Mirrors InMemoryIdempotencyService.Compose so the user-scoping
    // semantics carry through to the Redis backing store.
    private static string Compose(Guid userId, string key) => $"idempotency:{userId:N}:{key}";
}
```

DI swap (in `Infrastructure/DependencyInjection.cs`): the existing
`AddSingleton<IIdempotencyService, InMemoryIdempotencyService>()` becomes
`AddSingleton<IIdempotencyService, RedisIdempotencyService>()`. No caller
changes because the interface is preserved.

---

## 8. Event Types (Constants)

### File: `src/AgenticWorkforce.Domain/Events/EventTypes.cs`

```csharp
namespace AgenticWorkforce.Domain.Events;

public static class EventTypes
{
    // Project lifecycle
    public const string ProjectCreated = "project.created";
    public const string ProjectPaused = "project.paused";
    public const string ProjectResumed = "project.resumed";
    public const string ProjectArchived = "project.archived";

    // Task lifecycle
    public const string TaskCreated = "task.created";
    public const string TaskApproved = "task.approved";
    public const string TaskRejected = "task.rejected";
    public const string TaskQueued = "task.queued";
    public const string TaskStarted = "task.started";
    public const string TaskCompleted = "task.completed";
    public const string TaskFailed = "task.failed";
    public const string TaskCancelled = "task.cancelled";
    public const string TaskRetried = "task.retried";

    // Agent execution
    public const string AgentStarted = "agent.started";
    public const string AgentCompleted = "agent.completed";
    public const string AgentFailed = "agent.failed";
    public const string AgentToolCall = "agent.tool_call";
    public const string AgentTokenChunk = "agent.token_chunk";

    // Workflow
    public const string WorkflowStarted = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed = "workflow.failed";
    public const string WorkflowPaused = "workflow.paused";

    // Human input
    public const string HumanInputRequired = "human_input.required";
    public const string HumanInputProvided = "human_input.provided";   // Data includes HumanDecisionType (Approved/Rejected/Escalated/Overridden)
    public const string HumanInputTimedOut = "human_input.timed_out";

    // Budget
    public const string BudgetWarning = "budget.warning";
    public const string BudgetExhausted = "budget.exhausted";

    // Knowledge
    public const string LearningExtracted = "learning.extracted";
    public const string LearningRetracted = "learning.retracted";
    public const string ContextUpdated = "context.updated";

    // Session
    public const string SessionCreated = "session.created";
    public const string SessionCompleted = "session.completed";
    public const string MessageReceived = "message.received";
}
```

---

## 9. AppHost + Redis Wiring

The Aspire AppHost already exposes Redis to both processes; the connection
string `redis` arrives at each process via `ConnectionStrings:redis`.

```csharp
var redis = builder.AddRedis("redis").WithDataVolume();

var api = builder.AddProject<Projects.AgenticWorkforce_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.AgenticWorkforce_Worker>("worker")
    .WithReference(postgres)
    .WithReference(redis);
```

### `IConnectionMultiplexer` registration

Both the SignalR backplane and our own `RedisPubSubService` /
`RedisIdempotencyService` need a `StackExchange.Redis.IConnectionMultiplexer`.
Register it once as a singleton in `Infrastructure/DependencyInjection.cs`
so every consumer (and the SignalR backplane) shares one connection pool
rather than each opening its own:

```csharp
// Infrastructure/DependencyInjection.cs (additions)
services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var conn = cfg.GetConnectionString("redis")
        ?? throw new InvalidOperationException(
            "Connection string 'redis' is required (Aspire WithReference, env var, or Key Vault).");
    return ConnectionMultiplexer.Connect(conn);
});

services.AddSingleton<IRedisPubSubService, RedisPubSubService>();
services.AddSingleton<IEventPublisher, RedisEventPublisher>();
services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();   // replaces InMemoryIdempotencyService
```

And in `Api/Program.cs` the SignalR backplane reuses the same
configuration key so it joins the same Redis instance:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(
        builder.Configuration.GetConnectionString("redis")!,
        options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("agentic:");
        });

builder.Services.AddHostedService<SignalREventRelay>();

app.MapHub<ProjectHub>("/hubs/project");
```

Lazy resolution (the multiplexer is built inside the factory, not at
service registration time) matches the Postgres `NpgsqlDataSource` pattern
introduced in Phase 4, so `WebApplicationFactory` test overrides on the
`redis` connection string take effect.

---

## Decision Log — Phase 5 Plan Updates

These decisions resolve open issues spotted during the pre-build review.

| # | Decision | Why |
|---|---|---|
| 1 | `ProjectHub.JoinProject` / `JoinSession` call `IProjectAuthorizationService.EnsureRoleAsync` **before** joining the SignalR group. | Without it, any authenticated client could subscribe to any project's stream by guessing IDs (BOLA). |
| 2 | `RedisEventPublisher`: PostgreSQL is system of record; Redis publish failure logs a warning and returns success. | Throwing after a successful DB write would create phantom retries — the event would be recorded twice. Clients reconcile missed events via the events feed `since` cursor on reconnect. |
| 3 | `RedisPubSubService` uses `Channel.CreateBounded(1000, DropOldest)`. | Principle 19: bounded resources. The DB row is authoritative, so dropping an oldest in-memory copy under backpressure is acceptable. |
| 4 | `IRedisPubSubService` exposes both `SubscribeAsync` (literal) and `SubscribePatternAsync` (glob) — the relay needs the pattern variant. | Fixes a dangling reference: the relay called a method the original interface didn't declare. |
| 5 | SSE endpoints emit `: ping` every 15 s and send `X-Accel-Buffering: no`. | Most proxies (Nginx, Azure Front Door) drop idle connections at 30–60 s and buffer responses. |
| 6 | `SseTokenAuthHandler` extends `AuthenticationHandler<AuthenticationSchemeOptions>` and registers as a named scheme; stream endpoints opt in via `RequireAuthorization("SseStream")`. | The original sketch used the bare `IAuthenticationHandler` interface and never showed scheme registration — would have left tokens accepted on every endpoint or not at all. |
| 7 | `RedisIdempotencyService` preserves the `(Guid userId, string key, ct)` signature with key `idempotency:{userId:N}:{key}`. | Phase 3.5 added user-scoping to close a cross-user replay vulnerability; the Redis swap must not regress that. |
| 8 | `RedisPubSubService` calls `UnsubscribeAsync(channel, handler)` (handler-specific) rather than `UnsubscribeAsync(channel)` (removes-all). | The handler-omitted overload removes every callback for that channel; one SSE client's disconnect would have silently dropped every other concurrent subscriber. Found during adversarial review; regression test in `Events/RedisPubSubServiceTests`. |
| 9 | `SignalREventRelay` wraps `RunSubscriptionAsync` in a while-loop with exponential backoff (1 s → 1 min) so a faulted subscription iterator restarts instead of killing the relay. | Default `BackgroundServiceExceptionBehavior` would otherwise let the relay die silently while the host keeps running; live events would stop reaching all clients with no obvious symptom. |
| 10 | `RedisEventPublisher` is a transactional outbox: the `ProjectEvent` overload only `Add`s to the DbContext, the caller's existing `SaveChanges` commits, and `ProjectEventDispatchInterceptor` dispatches to Redis pub/sub in the post-commit hook. | Previously the publisher ran its own `SaveChanges` — endpoint flows like `repo.UpdateAsync(); publisher.PublishAsync();` committed in TWO transactions, leaving the business mutation persisted but the audit row missing if the second commit failed (a regulatory gap for a 7-year-retention audit log). |
| 11 | `RedisIdempotencyService.GetCachedResponseAsync` atomically CLAIMS the key via Redis `SET … NX` with a 30 s sentinel TTL before returning null. Second concurrent same-key request sees the sentinel and surfaces a 409. | Without a server-side claim, two simultaneous requests with the same idempotency key both miss the cache, both create resources, both return 201 — only sequential retries were deduplicated. Mirrored in `InMemoryIdempotencyService` via `ConcurrentDictionary.TryAdd`. |
| 12 | SSE token rides in `?token=` because `EventSource` cannot set headers. Token leakage to access logs is bounded by 30 s TTL + atomic single-use `GETDEL` + read-only scope. | The trade-off is explicit, documented inline in `SseTokenAuthHandler`, and the residual risk (an attacker with low-latency log access racing the legitimate handshake within 30 s, hijacking ONE read-only stream) is accepted on bounded-blast-radius grounds. |

---

## File Summary

### Files to CREATE

```
src/AgenticWorkforce.Api/Hubs/ProjectHub.cs
src/AgenticWorkforce.Api/Hubs/IProjectHubClient.cs
src/AgenticWorkforce.Api/Services/SignalREventRelay.cs
src/AgenticWorkforce.Api/Core/Auth/SseTokenAuthHandler.cs
src/AgenticWorkforce.Api/Features/Auth/CreateSseToken.cs
src/AgenticWorkforce.Api/Features/Events/StreamProjectEvents.cs
src/AgenticWorkforce.Api/Features/Events/StreamTaskEvents.cs
src/AgenticWorkforce.Api/Features/Events/StreamNotifications.cs
src/AgenticWorkforce.Domain/Events/EventTypes.cs
src/AgenticWorkforce.Infrastructure/Events/RedisEventPublisher.cs
src/AgenticWorkforce.Infrastructure/Events/RedisPubSubService.cs
src/AgenticWorkforce.Infrastructure/Events/IRedisPubSubService.cs
src/AgenticWorkforce.Infrastructure/Services/RedisIdempotencyService.cs
tests/AgenticWorkforce.Api.Tests.Integration/Hubs/ProjectHubTests.cs
tests/AgenticWorkforce.Api.Tests.Integration/Features/Events/SseStreamTests.cs
```

### Files to MODIFY

```
src/AgenticWorkforce.Api/Program.cs
  - AddSignalR().AddStackExchangeRedis(...) using shared connection string
  - AddScheme<…, SseTokenAuthHandler>("SseToken")
  - AddAuthorizationBuilder().AddPolicy("SseStream", ...)
  - AddHostedService<SignalREventRelay>()
  - MapHub<ProjectHub>("/hubs/project")
src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj
  - Microsoft.AspNetCore.SignalR.StackExchangeRedis
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs
  - AddSingleton<IConnectionMultiplexer>() with lazy factory (matches the
    NpgsqlDataSource pattern so test factory overrides apply)
  - AddSingleton<IRedisPubSubService, RedisPubSubService>()
  - AddSingleton<IEventPublisher, RedisEventPublisher>()
  - SWAP: AddSingleton<IIdempotencyService, RedisIdempotencyService>()
    (replaces the Phase 4 in-memory impl; the in-memory class can stay
    in source as a fallback / for unit tests)
src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj
  - StackExchange.Redis
Directory.Packages.props
  - StackExchange.Redis (version)
  - Microsoft.AspNetCore.SignalR.StackExchangeRedis (version)
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` — all tests pass
3. **Live event delivery.** Integration test: connect SignalR client →
   `JoinProject(memberProjectId)` → trigger a task mutation via the API →
   receive `task.created` on the hub.
4. **BOLA gate.** Integration test: authenticated user who is **not** a
   member of `projectId` calls `JoinProject(projectId)` → server throws
   `HubException` and the client never joins the group. No subsequent
   events for that project reach the connection.
5. **SSE content + headers.** `GET …/events/stream` returns
   `Content-Type: text/event-stream`, `X-Accel-Buffering: no`, and delivers
   a `: ping` comment within ~15 s of an idle connection.
6. **SSE token is single-use.** `POST /api/v1/auth/sse-token` returns a
   token; a `GET …/events/stream?token=…` succeeds; an immediate replay
   with the same token returns 401.
7. **User-scoped idempotency.** `RedisIdempotencyService` stores under
   `idempotency:{userId:N}:{key}`. Integration test: user A POSTs with
   header `X-Idempotency-Key: shared` → user B POSTs with the same header
   → B does NOT receive A's cached response (cross-user replay closed).
8. **Durability model (DB-first, pub/sub best-effort).** With Redis
   disconnected mid-test: `IEventPublisher.PublishAsync` returns success,
   the row is present in `project_events`, the publisher logs a warning
   (no exception bubbles to the caller). Reconnecting Redis does not
   replay missed events automatically — clients reconcile via the events
   feed with a `since` cursor.
9. **Bounded subscription buffer.** Unit test on `RedisPubSubService`:
   publish ≥ `SubscribeBufferSize + 100` messages faster than the
   consumer drains; the oldest entries are dropped, memory stays
   bounded, the consumer never throws.
10. **Pattern subscribe routes by channel.** Subscribe to `events:*`,
    publish to `events:A` and `events:B`, assert each message arrives
    tagged with its actual source channel.

---

## Goal Command

```
/goal Real-time event infrastructure complete: SignalR hub with Redis backplane delivers project events to connected clients. IEventPublisher persists events to DB and fans out via SignalR. SSE endpoints stream project events and task events. SSE token exchange provides single-use auth for EventSource. Redis idempotency service replaces in-memory stub. Verify: dotnet build exits 0, dotnet test exits 0 with integration test that connects SignalR, triggers a project mutation, and receives the event. Stop after 30 turns.
```
