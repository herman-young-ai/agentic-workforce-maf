using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Http;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Features.Events;

/// <summary>
/// SSE stream filtered to a single task within a project. Subscribes to
/// the project channel and forwards only messages whose <c>TaskId</c>
/// matches — Redis pub/sub doesn't support server-side filtering, so the
/// filter runs in-process. Used by the UI for live agent-output panes.
/// </summary>
public static class StreamTaskEvents
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/events/stream/tasks/{taskId:guid}", HandleAsync)
            .RequireAuthorization("SseStream")
            .WithTags("Events");

    private static async Task HandleAsync(
        Guid projectId,
        Guid taskId,
        HttpContext httpContext,
        IProjectAuthorizationService authz,
        ICurrentUserAccessor userAccessor,
        IRedisPubSubService redisPubSub,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        SseStreamWriter.WriteHeaders(httpContext);
        var heartbeatTask = SseStreamWriter.RunHeartbeatAsync(httpContext, ct);

        try
        {
            await foreach (var msg in redisPubSub.SubscribeAsync($"events:{projectId:N}", ct))
            {
                var evt = JsonSerializer.Deserialize<ProjectEventDto>(msg);
                if (evt is null || evt.TaskId != taskId) continue;
                await SseStreamWriter.WriteEventAsync(httpContext, evt.EventType, msg, ct);
            }
        }
        finally
        {
            // RunHeartbeatAsync handles cancellation internally; awaiting
            // surfaces unexpected faults instead of masking them.
            await heartbeatTask;
        }
    }
}
