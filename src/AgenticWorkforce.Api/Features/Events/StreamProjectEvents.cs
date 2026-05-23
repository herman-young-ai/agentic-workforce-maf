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

        await SseStreamWriter.PumpAsync(
            httpContext,
            RedisChannels.ProjectEvents(projectId),
            redisPubSub,
            msg =>
            {
                var evt = JsonSerializer.Deserialize<ProjectEventDto>(msg, WireJsonOptions.Default);
                return evt is null
                    ? SseStreamWriter.SseFrame.Skip
                    : SseStreamWriter.SseFrame.Send(evt.EventType, msg);
            },
            ct);
    }
}
