using System.Security.Cryptography;
using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using StackExchange.Redis;

namespace AgenticWorkforce.Api.Features.Auth;

/// <summary>
/// EventSource (the browser SSE API) cannot set request headers, so a
/// short-lived single-use token replaces the JWT for the URL form.
/// Workflow:
/// <list type="number">
///   <item>Client (already authenticated via JWT) POSTs here.</item>
///   <item>Server snapshots the user's claims into Redis under a 30 s key,
///         returns the token to the client.</item>
///   <item>Client opens <c>?token=…</c> against an SSE stream endpoint.</item>
///   <item><see cref="SseTokenAuthHandler"/> reads the token via GETDEL and
///         constructs the principal — single use; replay returns 401.</item>
/// </list>
/// </summary>
public static class CreateSseToken
{
    public record Response(string Token, int ExpiresInSeconds);

    private const int TokenTtlSeconds = 30;

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/auth/sse-token", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth");

    private static async Task<IResult> HandleAsync(
        ICurrentUserAccessor userAccessor,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        // 256 random bits → hex. Brute-force across a 30 s TTL is infeasible.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        // Snapshot the claims the SSE handler will reconstruct. This IS the
        // authorisation truth the stream endpoint sees — include Roles so
        // downstream role-based authz still works.
        var snapshot = JsonSerializer.Serialize(new
        {
            user.Id,
            user.Email,
            user.Roles
        });

        var db = redis.GetDatabase();
        await db.StringSetAsync(
            $"sse-token:{token}",
            snapshot,
            TimeSpan.FromSeconds(TokenTtlSeconds));

        return Results.Ok(new Response(token, TokenTtlSeconds));
    }
}
