using AgenticWorkforce.Infrastructure.Events;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Events;

/// <summary>
/// Regression coverage for <see cref="RedisPubSubService"/>.
/// </summary>
public class RedisPubSubServiceTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<AgenticWorkforce.Infrastructure.Data.AppDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Two concurrent subscribers on the same channel must both receive
    /// every message, and disconnecting one MUST NOT silently kill the
    /// other's subscription. Pre-fix, <c>UnsubscribeAsync(channel)</c>
    /// removed every handler attached to that channel — so cancelling
    /// subscriber A would have unhooked subscriber B's callback, and B
    /// would have stopped receiving published messages even though its
    /// <c>IAsyncEnumerable</c> was still being iterated.
    /// </summary>
    [Fact]
    public async Task TwoSubscribersOnSameChannel_OneCancels_OtherKeepsReceiving()
    {
        var pubSub = _factory.Services.GetRequiredService<IRedisPubSubService>();
        var channel = $"test:multisub:{Guid.NewGuid():N}";

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();

        // Drain each subscriber on a background task so the test thread
        // can drive publishes + cancellations in order.
        var receivedByA = new List<string>();
        var receivedByB = new List<string>();
        var taskA = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in pubSub.SubscribeAsync(channel, ctsA.Token))
                    receivedByA.Add(msg);
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        });
        var taskB = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in pubSub.SubscribeAsync(channel, ctsB.Token))
                    receivedByB.Add(msg);
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
        });

        // Both subscribers need to be registered before publishes start;
        // the test can't observe the exact moment SubscribeAsync's await
        // returns, so a small settle delay avoids race-induced flakes.
        await Task.Delay(200);

        // Phase 1: both subscribers receive the first message.
        await pubSub.PublishAsync(channel, "first");
        await WaitUntilAsync(() => receivedByA.Count >= 1 && receivedByB.Count >= 1);
        receivedByA.Should().ContainSingle().Which.Should().Be("first");
        receivedByB.Should().ContainSingle().Which.Should().Be("first");

        // Phase 2: cancel A. The pre-fix bug surfaced here — A's finally
        // ran UnsubscribeAsync(channel) which removed BOTH handlers.
        await ctsA.CancelAsync();
        await taskA;

        // Phase 3: B must still receive a fresh publish.
        await pubSub.PublishAsync(channel, "second");
        await WaitUntilAsync(() => receivedByB.Count >= 2);
        receivedByB.Should().Equal("first", "second");
        receivedByA.Should().Equal("first"); // A stopped before "second"

        await ctsB.CancelAsync();
        await taskB;
    }

    /// <summary>
    /// Polls the predicate until it returns true or the timeout elapses.
    /// Pub/sub delivery is asynchronous on Redis's side — we cannot block
    /// on the publish acknowledgement to know that the message has
    /// reached every subscriber's handler.
    /// </summary>
    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Expected pub/sub message did not arrive within the deadline.");
    }
}
