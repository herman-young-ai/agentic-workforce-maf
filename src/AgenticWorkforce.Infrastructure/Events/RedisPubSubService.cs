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
        var subscriber = redis.GetSubscriber();
        await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    public IAsyncEnumerable<string> SubscribeAsync(
        string channel, CancellationToken ct = default)
        => SubscribeLiteralAsync(RedisChannel.Literal(channel), ct);

    public IAsyncEnumerable<(string Channel, string Message)> SubscribePatternAsync(
        string pattern, CancellationToken ct = default)
        => SubscribePatternInternalAsync(RedisChannel.Pattern(pattern), ct);

    private async IAsyncEnumerable<string> SubscribeLiteralAsync(
        RedisChannel channel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateBounded<string>(new BoundedChannelOptions(SubscribeBufferSize)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        await subscriber.SubscribeAsync(channel, (_, message) =>
        {
            if (message.HasValue) queue.Writer.TryWrite(message!);
        });

        try
        {
            await foreach (var msg in queue.Reader.ReadAllAsync(ct))
                yield return msg;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    private async IAsyncEnumerable<(string Channel, string Message)> SubscribePatternInternalAsync(
        RedisChannel pattern,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var subscriber = redis.GetSubscriber();
        var queue = Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(SubscribeBufferSize)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        await subscriber.SubscribeAsync(pattern, (channel, message) =>
        {
            if (message.HasValue) queue.Writer.TryWrite((channel.ToString(), message!));
        });

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            await subscriber.UnsubscribeAsync(pattern);
        }
    }
}
