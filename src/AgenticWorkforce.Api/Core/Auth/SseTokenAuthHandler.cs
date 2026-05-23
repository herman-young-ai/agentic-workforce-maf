using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AgenticWorkforce.Infrastructure.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Authenticates SSE stream requests via the <c>?token=…</c> query
/// parameter produced by <c>POST /api/v1/auth/sse-token</c>. The handler
/// looks the token up in Redis with <c>GETDEL</c> — atomic read+delete —
/// so a token can be redeemed exactly once. A replay returns
/// <see cref="AuthenticateResult.Fail"/>; a missing token returns
/// <see cref="AuthenticateResult.NoResult"/> so JWT bearer (or any other
/// scheme on the same endpoint) gets a chance to authenticate.
///
/// <para><b>Security trade-off — token in URL</b></para>
/// The browser <c>EventSource</c> API can't set request headers, so the
/// auth credential rides on <c>?token=</c>. Reverse proxies, load
/// balancers and access-log pipelines typically log full request URLs
/// including query strings, so the raw token is visible to anyone who
/// can read those logs.
/// <para>
/// Residual exposure is bounded by three compensating controls:
/// </para>
/// <list type="bullet">
///   <item>30-second TTL on the Redis entry, so a token leaked to logs
///         is unredeemable after 30 s.</item>
///   <item>Atomic single-use via <c>GETDEL</c> — only the FIRST
///         redeemer wins; an attacker who reads logs within 30 s must
///         beat the legitimate client to the SSE endpoint.</item>
///   <item>Scope: the token unlocks read-only SSE streams; it cannot be
///         used for state-changing API calls.</item>
/// </list>
/// <para>
/// Net residual risk: an attacker with low-latency read access to
/// access logs racing a legitimate SSE handshake within a 30-second
/// window can hijack ONE stream of read-only events. Accepted on the
/// basis of the bounded blast radius. See R17 §SSE-token in the
/// security architecture for the formal acceptance record.
/// </para>
/// </summary>
public sealed class SseTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConnectionMultiplexer redis)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SseToken";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Query.TryGetValue("token", out var tokenRaw)
            || string.IsNullOrWhiteSpace(tokenRaw))
            return AuthenticateResult.NoResult();

        var db = redis.GetDatabase();
        // Atomic single-use: GETDEL returns the value AND removes the key
        // in one round-trip, so a replay of the same `?token=` returns null.
        var snapshotJson = await db.StringGetDeleteAsync($"sse-token:{tokenRaw}");
        if (!snapshotJson.HasValue)
            return AuthenticateResult.Fail("Invalid or already-redeemed SSE token.");

        SseTokenSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SseTokenSnapshot>(
                (string)snapshotJson!, WireJsonOptions.Default);
        }
        catch (JsonException)
        {
            return AuthenticateResult.Fail("Malformed SSE token payload.");
        }
        if (snapshot is null)
            return AuthenticateResult.Fail("Empty SSE token payload.");

        var claims = new List<Claim>
        {
            new("oid",                       snapshot.Id.ToString()),
            new(ClaimTypes.NameIdentifier,   snapshot.Id.ToString()),
            new(ClaimTypes.Email,            snapshot.Email),
            new("preferred_username",        snapshot.Email),
            new("name",                      snapshot.Email)
        };
        claims.AddRange(snapshot.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    // Snapshot shape persisted by CreateSseToken — must stay in sync.
    private sealed record SseTokenSnapshot(Guid Id, string Email, IReadOnlyList<string> Roles);
}
