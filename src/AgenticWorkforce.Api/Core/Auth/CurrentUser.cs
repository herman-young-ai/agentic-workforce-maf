using System.Security.Claims;
using AgenticWorkforce.Domain.Exceptions;

namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Resolved from JWT claims. Supports both Entra ID v1/v2 tokens and API key
/// authentication. Failures to resolve identity claims throw rather than
/// fall back to sentinel values (Principle 8: Fail Fast) — a token without
/// identity is not authenticated regardless of <see cref="ClaimsIdentity.IsAuthenticated"/>.
/// </summary>
public class CurrentUser
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = [];

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static CurrentUser FromClaimsPrincipal(ClaimsPrincipal principal) => new()
    {
        Id          = ResolveObjectId(principal),
        Email       = ResolveEmail(principal),
        DisplayName = ResolveDisplayName(principal),
        Roles       = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
    };

    private static Guid ResolveObjectId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("oid")
               ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
               ?? principal.FindFirstValue("uid")
               ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? throw new TokenInvalidException(
                    "Token has no object-identifier claim (oid, objectidentifier, uid, or nameidentifier required).");

        if (!Guid.TryParse(raw, out var id))
            throw new TokenInvalidException(
                $"Token object-identifier claim '{raw}' is not a valid Guid.");

        if (id == Guid.Empty)
            throw new TokenInvalidException(
                "Token object-identifier claim is the empty Guid; treat as anonymous.");

        return id;
    }

    private static string ResolveEmail(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue("upn")
        ?? throw new TokenInvalidException(
             "Token has no email-equivalent claim (email, preferred_username, or upn required).");

    private static string ResolveDisplayName(ClaimsPrincipal principal)
        => principal.FindFirstValue("name")
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? throw new TokenInvalidException(
             "Token has no display-name claim (name or givenname required).");
}

public interface ICurrentUserAccessor
{
    CurrentUser User { get; }
}

/// <summary>
/// Scoped per request: resolves the <see cref="CurrentUser"/> from the request's
/// <see cref="ClaimsPrincipal"/> on first access and caches it for the rest of
/// the request. Without the cache, every property access re-walks the claims
/// collection and allocates a fresh user.
/// </summary>
public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    private CurrentUser? _cached;

    public CurrentUser User => _cached ??= Resolve();

    private CurrentUser Resolve() =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
            ? CurrentUser.FromClaimsPrincipal(principal)
            : throw new UnauthorizedException();
}
