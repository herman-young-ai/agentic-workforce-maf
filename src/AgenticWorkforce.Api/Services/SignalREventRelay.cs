using System.Text.Json;
using AgenticWorkforce.Api.Hubs;
using AgenticWorkforce.Infrastructure.Events;
using Microsoft.AspNetCore.SignalR;

namespace AgenticWorkforce.Api.Services;

/// <summary>
/// Bridges Redis pub/sub onto SignalR groups so the Worker (or any other
/// publisher process) can push events without taking a dependency on
/// SignalR types. The relay subscribes to <c>events:*</c> via a pattern
/// subscription and dispatches each message to the matching
/// <c>project:{id}</c> group.
///
/// <para><b>Resilience</b></para>
/// Per-message <c>try/catch</c>: one malformed payload or a transient hub
/// send failure must not kill the relay loop. Failures are logged; the
/// authoritative <c>project_events</c> row remains in PostgreSQL so a
/// reconnecting client can replay via the events feed.
/// </summary>
internal sealed class SignalREventRelay(
    IRedisPubSubService redisPubSub,
    IHubContext<ProjectHub, IProjectHubClient> hubContext,
    ILogger<SignalREventRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation(
            "SignalR event relay starting — subscribing to {Pattern}",
            RedisChannels.AllProjectEventsPattern);

        await foreach (var (channel, message) in redisPubSub.SubscribePatternAsync(
            RedisChannels.AllProjectEventsPattern, ct))
        {
            try
            {
                var projectIdSegment = RedisChannels.TryExtractProjectIdSegment(channel);
                if (projectIdSegment is null)
                {
                    logger.LogWarning("Ignoring message on unexpected channel {Channel}", channel);
                    continue;
                }

                var evt = JsonSerializer.Deserialize<ProjectEventDto>(message, WireJsonOptions.Default);
                if (evt is null)
                {
                    logger.LogWarning("Dropping malformed event from {Channel}", channel);
                    continue;
                }

                await hubContext.Clients
                    .Group(HubGroups.Project(projectIdSegment))
                    .ProjectEvent(evt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to relay event from {Channel}; payload preserved in project_events",
                    channel);
            }
        }

        logger.LogInformation("SignalR event relay stopped");
    }
}
