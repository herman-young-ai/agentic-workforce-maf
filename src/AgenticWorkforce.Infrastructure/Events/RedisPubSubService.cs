using System.Runtime.CompilerServices;
using System.Threading.Channels;
using StackExchange.Redis;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Adapter between Redis pub/sub and the IAsyncEnumerable contract callers
/// consume. Each active subscription owns a bounded in-memory channel: a
/// slow consumer drops the OLDEST buffered message under backpressure
/// rather than letting memory grow without bound (Principle 19). Because
/// <c>project_events</c> is the durable system of record, a dropped
/// pub/sub message is a delayed event for clients, not a lost one — they
/// reconcile via the events feed with a <c>since</c> cursor on reconnect.
/// </summary>
internal sealed class RedisPubSubService(IConnectionMultiplexer redis) : IRedisPubSubService
{
    // ~1000 events at ~1KB each caps per-subscription memory at ~1MB.
    private const int SubscribeBufferSize = 1000;

    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var subscriber = redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    public IAsyncEnumerable<string> SubscribeAsync(
        string channel, CancellationToken ct = default)
        => SubscribeCoreAsync(RedisChannel.Literal(channel), (_, msg) => (string)msg!, ct);

    public IAsyncEnumerable<(string Channel, string Message)> SubscribePatternAsync(
        string pattern, CancellationToken ct = default)
        => SubscribeCoreAsync(RedisChannel.Pattern(pattern), (ch, msg) => (ch.ToString(), (string)msg!), ct);

    /// <summary>
    /// Generic subscribe core shared by both literal and pattern flavours.
    /// Owns the bounded channel, the StackExchange.Redis subscription
    /// lifetime, and the unsubscribe-on-dispose contract — the public
    /// methods just supply a <see cref="RedisChannel"/> and a mapper from
    /// <c>(channel, value)</c> to the consumer's element shape.
    /// </summary>
    private async IAsyncEnumerable<T> SubscribeCoreAsync<T>(
        RedisChannel channel,
        Func<RedisChannel, RedisValue, T> map,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateBounded<T>(new BoundedChannelOptions(SubscribeBufferSize)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        await subscriber.SubscribeAsync(channel, (ch, message) =>
        {
            if (message.HasValue) queue.Writer.TryWrite(map(ch, message));
        });

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }
}
