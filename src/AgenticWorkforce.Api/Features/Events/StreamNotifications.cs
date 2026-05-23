using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Http;
using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Features.Events;

/// <summary>
/// Per-user notification stream — spans every project the user has access
/// to. Authorisation is intrinsic: the channel key includes the connected
/// user's own id, so there's no project-id to verify membership for.
/// Publishers (Phase 6+ — e.g. budget warnings, knowledge-promotion
/// decisions) publish to <c>user:{userId:N}:notifications</c>.
/// </summary>
public static class StreamNotifications
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/notifications/stream", HandleAsync)
            .RequireAuthorization("SseStream")
            .WithTags("Notifications");

    private static Task HandleAsync(
        HttpContext httpContext,
        ICurrentUserAccessor userAccessor,
        IRedisPubSubService redisPubSub,
        CancellationToken ct)
    {
        // Forward the raw payload — the publisher already shapes it per
        // the notification contract; this endpoint is a pure passthrough.
        return SseStreamWriter.PumpAsync(
            httpContext,
            RedisChannels.UserNotifications(userAccessor.User.Id),
            redisPubSub,
            msg => SseStreamWriter.SseFrame.Send("notification", msg),
            ct);
    }
}
