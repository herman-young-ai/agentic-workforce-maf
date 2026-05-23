using System.Text.Json;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Events;
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
///
/// <para><b>Concurrent-request claim</b></para>
/// <see cref="GetCachedResponseAsync"/> atomically CLAIMS the key when
/// no entry exists (Redis <c>SET … NX</c> with a short sentinel TTL).
/// This closes the TOCTOU window that would otherwise let two
/// simultaneous requests with the same key both miss the cache and both
/// create duplicate resources. Second concurrent request sees the
/// sentinel and surfaces a 409 — by the time it retries, the first
/// request has cached the real response.
/// </summary>
internal sealed class RedisIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    // Real responses live for 24h; in-flight claims expire after 30s so
    // a request that errors out without caching doesn't lock the key
    // indefinitely.
    private static readonly TimeSpan Ttl       = TimeSpan.FromHours(24);
    private static readonly TimeSpan ClaimTtl  = TimeSpan.FromSeconds(30);

    // Sentinel value stored in Redis to mark "request in flight". A
    // syntactically-invalid-JSON token makes accidental deserialization
    // attempts fail fast rather than masquerade as a cached response.
    internal const string ClaimSentinel = "__claim_in_flight__";

    public async Task<T?> GetCachedResponseAsync<T>(
        Guid userId, string key, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var composed = Compose(userId, key);

        // Atomic claim: try to write the sentinel iff no entry exists.
        // When.NotExists is the Redis SETNX semantic and avoids any
        // server-side TOCTOU window.
        var claimed = await db.StringSetAsync(
            composed, ClaimSentinel, ClaimTtl, When.NotExists);
        if (claimed) return default;  // we own the work

        // Something is already there — either a real cached response or
        // another in-flight claim.
        var value = await db.StringGetAsync(composed);
        if (!value.HasValue) return default;        // expired between SET-NX and GET
        if (value == ClaimSentinel)
            throw new ConflictException(
                "Idempotency",
                "another request with the same idempotency key is already in flight; retry shortly");

        return JsonSerializer.Deserialize<T>((string)value!, WireJsonOptions.Default);
    }

    public async Task CacheResponseAsync<T>(
        Guid userId, string key, T response, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        // Unconditional set: overwrites our own claim sentinel with the
        // real response and extends the TTL to the long-lived value.
        await db.StringSetAsync(
            Compose(userId, key),
            JsonSerializer.Serialize(response, WireJsonOptions.Default),
            Ttl);
    }

    private static string Compose(Guid userId, string key) => $"idempotency:{userId:N}:{key}";
}
