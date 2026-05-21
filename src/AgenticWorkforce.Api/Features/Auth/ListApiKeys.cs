using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Auth;

public static class ListApiKeys
{
    public record Response(
        Guid Id,
        string Name,
        string KeyPrefix,
        DateTime? ExpiresAt,
        DateTime? RevokedAt,
        DateTime? LastUsedAt,
        string? Scopes,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/auth/me/api-keys", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth")
            ;

    private static async Task<IResult> HandleAsync(
        ICurrentUserAccessor userAccessor,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        var keys = await db.ApiKeys
            .AsNoTracking()
            .Where(k => k.UserId == user.Id && k.RevokedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new Response(k.Id, k.Name, k.KeyPrefix, k.ExpiresAt, k.RevokedAt, k.LastUsedAt, k.Scopes, k.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(keys);
    }
}
