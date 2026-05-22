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
/// </summary>
internal sealed class InMemoryIdempotencyService : IIdempotencyService
{
    private readonly ConcurrentDictionary<string, (object Response, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

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

    private static string Compose(Guid userId, string key) => $"{userId:N}:{key}";
}
