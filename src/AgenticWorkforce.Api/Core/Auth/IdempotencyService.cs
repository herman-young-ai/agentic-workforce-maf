using System.Collections.Concurrent;

namespace AgenticWorkforce.Api.Core.Auth;

public interface IIdempotencyService
{
    Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken ct = default);
    Task CacheResponseAsync<T>(string key, T response, CancellationToken ct = default);
}

/// <summary>
/// In-memory idempotency store for Phase 3. Redis-backed implementation arrives in Phase 5.
/// Entries survive across requests for the process lifetime (24 h TTL).
/// </summary>
internal sealed class InMemoryIdempotencyService : IIdempotencyService
{
    private readonly ConcurrentDictionary<string, (object Response, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public Task<T?> GetCachedResponseAsync<T>(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return Task.FromResult((T?)entry.Response);
        return Task.FromResult<T?>(default);
    }

    public Task CacheResponseAsync<T>(string key, T response, CancellationToken ct = default)
    {
        _cache[key] = (response!, DateTime.UtcNow.Add(Ttl));
        return Task.CompletedTask;
    }
}
