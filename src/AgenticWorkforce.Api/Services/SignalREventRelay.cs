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
/// Two layers of fault tolerance:
/// <list type="bullet">
///   <item>
///     Per-message <c>try/catch</c>: one malformed payload or a transient
///     hub send failure must not break the subscription loop.
///   </item>
///   <item>
///     Per-subscription <c>try/catch</c> with exponential backoff: if the
///     subscription iterator itself faults (multiplexer reset, network
///     partition that exhausts retries, anything that terminates the
///     <c>IAsyncEnumerable</c>), the relay sleeps and resubscribes. The
///     default <c>BackgroundServiceExceptionBehavior</c> would otherwise
///     stop the relay — and silently, since the host keeps running.
///   </item>
/// </list>
/// The authoritative <c>project_events</c> row remains in PostgreSQL so a
/// reconnecting client can replay via the events feed; live delivery is
/// best-effort by design.
/// </summary>
internal sealed class SignalREventRelay(
    IRedisPubSubService redisPubSub,
    IHubContext<ProjectHub, IProjectHubClient> hubContext,
    ILogger<SignalREventRelay> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff     = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var backoff = InitialBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSubscriptionAsync(ct);
                return; // RunSubscriptionAsync only exits cleanly when ct cancels.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "SignalR event relay subscription faulted; restarting in {BackoffSeconds:F1}s",
                    backoff.TotalSeconds);

                try
                {
                    await Task.Delay(backoff, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = TimeSpan.FromMilliseconds(
                    Math.Min(backoff.TotalMilliseconds * 2, MaxBackoff.TotalMilliseconds));
            }
        }
    }

    private async Task RunSubscriptionAsync(CancellationToken ct)
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

        logger.LogInformation("SignalR event relay subscription completed normally");
    }
}
