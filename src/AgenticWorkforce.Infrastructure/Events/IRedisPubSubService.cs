namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Wraps the StackExchange.Redis <c>ISubscriber</c> behind two narrow
/// methods (publish + subscribe) so consumers in this assembly never see
/// raw Redis types. The interface lives in Infrastructure because it
/// returns/accepts only primitives — keeping it out of Domain avoids
/// pulling StackExchange.Redis into the platform-pure layer.
/// </summary>
public interface IRedisPubSubService
{
    /// <summary>
    /// Publishes <paramref name="message"/> to the literal Redis channel
    /// <paramref name="channel"/>. Fire-and-forget at the application level:
    /// failures bubble up as exceptions so callers can choose the right
    /// fail-mode for their context.
    /// </summary>
    Task PublishAsync(string channel, string message, CancellationToken ct = default);

    /// <summary>
    /// Streams messages from a single literal Redis channel. The returned
    /// sequence completes when <paramref name="ct"/> is cancelled; the
    /// underlying Redis subscription is released in the enumerator's
    /// finally block.
    /// </summary>
    IAsyncEnumerable<string> SubscribeAsync(string channel, CancellationToken ct = default);

    /// <summary>
    /// Streams messages from any channel matching <paramref name="pattern"/>
    /// (glob syntax, e.g. <c>"events:*"</c>). Each yielded item carries
    /// the actual channel the message arrived on so consumers can route
    /// without re-parsing the payload.
    /// </summary>
    IAsyncEnumerable<(string Channel, string Message)> SubscribePatternAsync(
        string pattern, CancellationToken ct = default);
}
