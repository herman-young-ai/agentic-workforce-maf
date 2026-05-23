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
                // The reset callback fires on the first message of a fresh
                // subscription — that's the signal "this subscription is
                // genuinely healthy". Without it, a long-lived subscription
                // that faults after weeks of success would restart at the
                // cached high backoff inherited from a prior fault burst.
                await RunSubscriptionAsync(onHealthy: () => backoff = InitialBackoff, ct);
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

    private async Task RunSubscriptionAsync(Action onHealthy, CancellationToken ct)
    {
        logger.LogInformation(
            "SignalR event relay starting — subscribing to {Pattern}",
            RedisChannels.AllProjectEventsPattern);

        var healthyReported = false;

        await foreach (var (channel, message) in redisPubSub.SubscribePatternAsync(
            RedisChannels.AllProjectEventsPattern, ct))
        {
            // First message means the subscription handshake succeeded
            // and Redis is delivering — reset the caller's backoff state.
            if (!healthyReported)
            {
                onHealthy();
                healthyReported = true;
            }

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
