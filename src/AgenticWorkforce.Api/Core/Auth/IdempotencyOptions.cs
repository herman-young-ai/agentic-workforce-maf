namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Tunable TTLs for <see cref="IIdempotencyService"/> implementations.
/// Bound from configuration section <c>Idempotency</c> in Program.cs.
///
/// <para><b>Claim TTL is the request-handler budget</b></para>
/// The claim sentinel sits in Redis for <see cref="ClaimTtlSeconds"/> while
/// the originating request runs. A concurrent request with the same key
/// sees the sentinel and gets 409. If the original request takes longer
/// than the claim TTL, the sentinel expires and a concurrent retry can
/// re-claim — both end up creating resources. Set this to the upper-bound
/// expected synchronous handler duration for your slowest idempotent
/// endpoint (document uploads, agent dispatches, etc.).
///
/// <para><b>Response TTL is the replay window</b></para>
/// Once an endpoint completes and caches its response, the cached value
/// answers retries for <see cref="ResponseTtlHours"/>. After expiry the
/// key disappears and a new request starts fresh.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// How long the in-flight claim sentinel remains valid before
    /// expiring. Defaults to 30 s — fine for sub-second JSON endpoints,
    /// too short for multi-minute agent dispatches. Configurable so ops
    /// can tune without code changes.
    /// </summary>
    public int ClaimTtlSeconds { get; set; } = 30;

    /// <summary>
    /// How long a completed response stays cached for retry deduplication.
    /// Defaults to 24 h matching the original Phase 3.5 contract.
    /// </summary>
    public int ResponseTtlHours { get; set; } = 24;
}
