using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Http;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Features.Events;

/// <summary>
/// Server-sent event stream of every <c>project_events</c> row for a
/// project. Authorised via the <c>SseStream</c> policy (JWT bearer or
/// single-use SSE token); project membership is checked here in the
/// handler so the policy gate isn't load-bearing for BOLA.
/// </summary>
public static class StreamProjectEvents
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/events/stream", HandleAsync)
            .RequireAuthorization("SseStream")
            .WithTags("Events");

    private static async Task HandleAsync(
        Guid projectId,
        HttpContext httpContext,
        IProjectAuthorizationService authz,
        ICurrentUserAccessor userAccessor,
        IRedisPubSubService redisPubSub,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        SseStreamWriter.WriteHeaders(httpContext);

        // The heartbeat runs concurrently with the event loop so an idle
        // stream never trips the proxy idle-timeout.
        var heartbeatTask = SseStreamWriter.RunHeartbeatAsync(httpContext, ct);

        try
        {
            await foreach (var msg in redisPubSub.SubscribeAsync($"events:{projectId:N}", ct))
            {
                var evt = JsonSerializer.Deserialize<ProjectEventDto>(msg);
                if (evt is null) continue;
                await SseStreamWriter.WriteEventAsync(httpContext, evt.EventType, msg, ct);
            }
        }
        finally
        {
            // The heartbeat completes normally on cancellation (handled
            // inside RunHeartbeatAsync); awaiting surfaces any unexpected
            // fault rather than masking it.
            await heartbeatTask;
        }
    }
}
