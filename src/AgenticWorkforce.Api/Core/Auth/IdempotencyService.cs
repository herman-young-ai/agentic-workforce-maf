using System.Collections.Concurrent;

namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Caches the response of a request keyed on the requesting user AND the
/// idempotency header. User-scoping is required so that one user cannot
/// replay another user's response by submitting their key (cross-user replay
/// vulnerability). All cache keys are composed as <c>{userId}:{key}</c>
/// internally — callers pass the user id explicitly, never implicitly.
/// </summary>
public interface IIdempotencyService
{
    Task<T?> GetCachedResponseAsync<T>(Guid userId, string key, CancellationToken ct = default);
    Task CacheResponseAsync<T>(Guid userId, string key, T response, CancellationToken ct = default);
}

/// <summary>
/// In-memory idempotency store for Phase 3.5. Replaced by Redis-backed
/// implementation in Phase 5 so the cache survives Api replica scale-out
/// (per-replica state breaks idempotency under retries that hit a different
/// pod).
/// <para>
/// The cache used to grow without bound — expired entries were only evicted
/// when the SAME key was queried again, which by definition won't happen for
/// an unused idempotency key. A timer now sweeps expired entries every
/// <see cref="SweepInterval"/>. <see cref="Dispose"/> stops the timer on
/// host shutdown.
/// </para>
/// </summary>
internal sealed class InMemoryIdempotencyService : IIdempotencyService, IDisposable
{
    private static readonly TimeSpan Ttl           = TimeSpan.FromHours(24);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (object Response, DateTime ExpiresAt)> _cache = new();
    private readonly Timer _sweepTimer;
    private bool _disposed;

    public InMemoryIdempotencyService()
    {
        _sweepTimer = new Timer(_ => SweepExpired(), state: null,
            dueTime: SweepInterval, period: SweepInterval);
    }

    public Task<T?> GetCachedResponseAsync<T>(Guid userId, string key, CancellationToken ct = default)
    {
        var composed = Compose(userId, key);
        if (_cache.TryGetValue(composed, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return Task.FromResult((T?)entry.Response);
        return Task.FromResult<T?>(default);
    }

    public Task CacheResponseAsync<T>(Guid userId, string key, T response, CancellationToken ct = default)
    {
        _cache[Compose(userId, key)] = (response!, DateTime.UtcNow.Add(Ttl));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes entries whose <c>ExpiresAt</c> has passed. Exposed internal
    /// for tests so the sweep can be exercised without spinning the timer.
    /// </summary>
    internal int SweepExpired()
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAt <= now
                && _cache.TryRemove(new KeyValuePair<string, (object, DateTime)>(kvp.Key, kvp.Value)))
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

    private static string Compose(Guid userId, string key) => $"{userId:N}:{key}";
}
