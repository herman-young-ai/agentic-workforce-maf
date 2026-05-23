using System.Text.Json;
using StackExchange.Redis;

namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Replaces <see cref="InMemoryIdempotencyService"/> with a Redis backing
/// store so the cache survives Api replica scale-out — an idempotent
/// retry hitting a different pod must still see the prior response, which
/// per-replica state breaks.
///
/// <para><b>User scoping is mandatory</b></para>
/// Key composition mirrors the in-memory implementation:
/// <c>idempotency:{userId:N}:{key}</c>. The Phase 3.5 review added the
/// userId component to close a cross-user replay vulnerability — a single
/// header-only key would let user B replay user A's cached response
/// (including the <c>Location</c> of a created resource) by submitting
/// A's idempotency header. Do not regress this.
/// </summary>
internal sealed class RedisIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<T?> GetCachedResponseAsync<T>(
        Guid userId, string key, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Compose(userId, key));
        // Cast to string disambiguates between the (string) and
        // (ReadOnlySpan<byte>) overloads of JsonSerializer.Deserialize.
        return value.HasValue ? JsonSerializer.Deserialize<T>((string)value!) : default;
    }

    public async Task CacheResponseAsync<T>(
        Guid userId, string key, T response, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.StringSetAsync(
            Compose(userId, key),
            JsonSerializer.Serialize(response),
            Ttl);
    }

    private static string Compose(Guid userId, string key) => $"idempotency:{userId:N}:{key}";
}
