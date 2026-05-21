using System.Security.Claims;
using AgenticWorkforce.Domain.Exceptions;

namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// Resolved from JWT claims. Supports both Entra ID v1/v2 tokens
/// and API key authentication.
/// Adopted from SecurityBff reference, extended for agent identities.
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
        Id = ResolveObjectId(principal),
        Email = principal.FindFirstValue(ClaimTypes.Email)
             ?? principal.FindFirstValue("preferred_username")
             ?? principal.FindFirstValue("upn")
             ?? string.Empty,
        DisplayName = principal.FindFirstValue("name")
                   ?? principal.FindFirstValue(ClaimTypes.Name)
                   ?? string.Empty,
        Roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
    };

    private static Guid ResolveObjectId(ClaimsPrincipal principal)
    {
        string? raw =
            principal.FindFirstValue("oid")
         ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
         ?? principal.FindFirstValue("uid")
         ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}

public interface ICurrentUserAccessor
{
    CurrentUser User { get; }
}

/// <summary>
/// Scoped per request: resolves the <see cref="CurrentUser"/> from the request's
/// <see cref="ClaimsPrincipal"/> on first access and caches it for the rest of
/// the request. Without the cache, every property access re-walks the claims
/// collection and allocates a fresh user — handlers typically read .Id and
/// .Roles many times per request.
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
