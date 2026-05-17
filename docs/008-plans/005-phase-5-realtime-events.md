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
public class ProjectHub : Hub
{
    // Client joins a project channel
    public async Task JoinProject(Guid projectId)
    {
        // Verify membership via IProjectAuthorizationService
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");
    }

    public async Task LeaveProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project:{projectId}");
    }

    public async Task JoinSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    public async Task LeaveSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    public override async Task OnConnectedAsync()
    {
        // Add to user-specific group for notifications
        var userId = Context.User?.FindFirst("oid")?.Value;
        if (userId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await base.OnConnectedAsync();
    }
}
```

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
        // 1. Persist to DB (append-only)
        db.ProjectEvents.Add(evt);
        await db.SaveChangesAsync(ct);

        // 2. Publish to Redis pub/sub (Api's relay picks this up)
        var payload = JsonSerializer.Serialize(MapToDto(evt));
        await redisPubSub.PublishAsync($"events:{evt.ProjectId}", payload, ct);

        logger.LogInformation(
            "Published {EventType} to project:{ProjectId}",
            evt.EventType, evt.ProjectId);
    }

    public async Task PublishAsync(string channel, string eventType, object data, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { eventType, data, timestamp = DateTime.UtcNow });
        await redisPubSub.PublishAsync(channel, payload, ct);
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
        // Subscribe to all project event channels via pattern
        await foreach (var (channel, message) in redisPubSub.SubscribePatternAsync("events:*", ct))
        {
            var projectId = channel.Replace("events:", "");
            var evt = JsonSerializer.Deserialize<ProjectEventDto>(message);
            if (evt != null)
            {
                await hubContext.Clients
                    .Group($"project:{projectId}")
                    .ProjectEvent(evt);
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

```csharp
public static class StreamProjectEvents
{
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/projects/{projectId}/events/stream", HandleAsync)
            .WithTags("Events");
    }

    private static async Task HandleAsync(
        Guid projectId,
        HttpContext httpContext,
        IProjectAuthorizationService authz,
        ICurrentUserAccessor userAccessor,
        CancellationToken ct)
    {
        // Auth via SSE token (query param) or standard bearer
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        // Subscribe to Redis pub/sub for this project
        var channel = $"events:{projectId}";
        await foreach (var evt in SubscribeAsync(channel, ct))
        {
            await httpContext.Response.WriteAsync(
                $"event: {evt.EventType}\ndata: {JsonSerializer.Serialize(evt)}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }
    }
}
```

### File: `src/AgenticWorkforce.Api/Features/Events/StreamTaskEvents.cs`

SSE scoped to a single task execution — used by the UI to show live agent output:

```csharp
app.MapGet("/api/v1/projects/{projectId}/events/stream/tasks/{taskId}", HandleAsync)
    .WithTags("Events");
```

### File: `src/AgenticWorkforce.Api/Features/Events/StreamNotifications.cs`

Per-user notification stream (all projects):

```csharp
app.MapGet("/api/v1/notifications/stream", HandleAsync)
    .WithTags("Notifications");
```

---

## 5. SSE Token Exchange

### File: `src/AgenticWorkforce.Api/Features/Auth/CreateSseToken.cs`

EventSource API cannot set HTTP headers. The client exchanges a JWT for a short-lived Redis token:

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
        var token = Guid.NewGuid().ToString("N");
        var db = redis.GetDatabase();

        // Store: token → user claims JSON, 30s TTL, single-use via GETDEL
        var claims = JsonSerializer.Serialize(new { user.Id, user.Email, user.Roles });
        await db.StringSetAsync($"sse-token:{token}", claims, TimeSpan.FromSeconds(30));

        return Results.Ok(new Response(token, 30));
    }
}
```

### SSE Auth Middleware

For SSE endpoints, resolve user from `?token=` query param via Redis GETDEL:

```csharp
// Api/Core/Auth/SseTokenAuthHandler.cs
public class SseTokenAuthHandler(IConnectionMultiplexer redis) : IAuthenticationHandler
{
    // If ?token= present, GETDEL from Redis, construct ClaimsPrincipal
    // Single-use: token is deleted on first read
}
```

---

## 6. Redis Pub/Sub Service

### File: `src/AgenticWorkforce.Infrastructure/Events/RedisPubSubService.cs`

Wraps `ISubscriber` for publishing and subscribing:

```csharp
internal sealed class RedisPubSubService(IConnectionMultiplexer redis) : IRedisPubSubService
{
    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    public async IAsyncEnumerable<string> SubscribeAsync(
        string channel, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateUnbounded<string>();

        await subscriber.SubscribeAsync(RedisChannel.Literal(channel), (_, message) =>
        {
            if (message.HasValue)
                queue.Writer.TryWrite(message!);
        });

        try
        {
            await foreach (var msg in queue.Reader.ReadAllAsync(ct))
                yield return msg;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));
        }
    }
}
```

---

## 7. Idempotency Service (Redis)

Replace the Phase 3 in-memory stub with Redis:

### File: `src/AgenticWorkforce.Infrastructure/Services/RedisIdempotencyService.cs`

```csharp
internal sealed class RedisIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync($"idempotency:{key}");
        return value.HasValue ? JsonSerializer.Deserialize<T>(value!) : default;
    }

    public async Task CacheResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync($"idempotency:{key}", JsonSerializer.Serialize(response), Ttl);
    }
}
```

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

## 9. AppHost Wiring

Update `src/AgenticWorkforce.AppHost/Program.cs` to expose Redis connection to both Api and Worker:

```csharp
var redis = builder.AddRedis("redis")
    .WithDataVolume();

var api = builder.AddProject<Projects.AgenticWorkforce_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.AgenticWorkforce_Worker>("worker")
    .WithReference(postgres)
    .WithReference(redis);
```

(Already mostly correct — verify Redis connection string name matches what Infrastructure expects.)

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
src/AgenticWorkforce.Api/Program.cs — Add SignalR, Redis, hub mapping
src/AgenticWorkforce.Api/AgenticWorkforce.Api.csproj — Add SignalR Redis package
src/AgenticWorkforce.Infrastructure/DependencyInjection.cs — Register event services
src/AgenticWorkforce.Infrastructure/AgenticWorkforce.Infrastructure.csproj — Add StackExchange.Redis
Directory.Packages.props — Add Redis + SignalR package versions
```

---

## Verification Criteria

1. `dotnet build AgenticWorkforce.slnx` exits 0
2. `dotnet test AgenticWorkforce.slnx` — all tests pass
3. Integration test: connect SignalR client → join project group → create task via API → receive `task.created` event on hub
4. SSE endpoint returns `text/event-stream` content type and delivers events
5. SSE token exchange: POST gets token, GET with `?token=` authenticates and invalidates token (single-use)
6. Idempotency service uses Redis (24h TTL, same key returns same response)
7. `IEventPublisher` persists to DB AND fans out via SignalR in a single call
8. No event is silently dropped — if DB write succeeds but SignalR fails, log error (don't throw)

---

## Goal Command

```
/goal Real-time event infrastructure complete: SignalR hub with Redis backplane delivers project events to connected clients. IEventPublisher persists events to DB and fans out via SignalR. SSE endpoints stream project events and task events. SSE token exchange provides single-use auth for EventSource. Redis idempotency service replaces in-memory stub. Verify: dotnet build exits 0, dotnet test exits 0 with integration test that connects SignalR, triggers a project mutation, and receives the event. Stop after 30 turns.
```
