using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
            snapshot = JsonSerializer.Deserialize<SseTokenSnapshot>((string)snapshotJson!);
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
