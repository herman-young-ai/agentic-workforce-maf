using System.Collections.Concurrent;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Queries;

namespace AgenticWorkforce.Infrastructure.Repositories;

/// <summary>
/// Phase 4 in-memory stub. Records enqueued messages so <c>GetStatusAsync</c>
/// can answer for known IDs (always with state <see cref="ExecutionState.Pending"/>
/// because no consumer is wired yet). Phase 5/8 replaces this with a Redis
/// Streams client; the interface is stable across the swap.
/// <para>
/// As a singleton, the underlying dictionary would otherwise grow without
/// bound. A timer sweeps entries older than <see cref="EntryTtl"/> every
/// <see cref="SweepInterval"/>. A swept ID becomes indistinguishable from
/// "never enqueued" — acceptable for a stub whose entries never transition
/// away from <c>Pending</c>; once the Redis Streams client lands the TTL is
/// owned by Redis.
/// </para>
/// </summary>
internal sealed class InMemoryExecutionRepository : IExecutionRepository, IDisposable
{
    private static readonly TimeSpan EntryTtl      = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, (ExecutionStatus Status, DateTime CreatedAt)> _enqueued = new();
    private readonly Timer _sweepTimer;
    private bool _disposed;

    public InMemoryExecutionRepository()
    {
        _sweepTimer = new Timer(_ => SweepExpired(), state: null,
            dueTime: SweepInterval, period: SweepInterval);
    }

    public Task<Guid> EnqueueDispatchAsync(
        Guid projectId,
        IReadOnlyList<Guid> taskIds,
        Guid requestedById,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _enqueued[id] = (new ExecutionStatus(id, projectId, ExecutionState.Pending), DateTime.UtcNow);
        return Task.FromResult(id);
    }

    public Task<Guid> EnqueueAdHocAsync(
        Guid projectId,
        string objective,
        Guid requestedById,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        _enqueued[id] = (new ExecutionStatus(id, projectId, ExecutionState.Pending), DateTime.UtcNow);
        return Task.FromResult(id);
    }

    public Task<ExecutionStatus?> GetStatusAsync(Guid executionId, CancellationToken ct = default)
        => Task.FromResult(_enqueued.TryGetValue(executionId, out var entry) ? entry.Status : null);

    /// <summary>
    /// Removes entries older than <see cref="EntryTtl"/>. Exposed internal
    /// so tests can exercise the sweep without waiting on the timer.
    /// </summary>
    internal int SweepExpired()
    {
        var cutoff = DateTime.UtcNow - EntryTtl;
        var removed = 0;
        foreach (var kvp in _enqueued)
        {
            if (kvp.Value.CreatedAt <= cutoff
                && _enqueued.TryRemove(new KeyValuePair<Guid, (ExecutionStatus, DateTime)>(kvp.Key, kvp.Value)))
            {
                removed++;
            }
        }
        return removed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
    }
}
