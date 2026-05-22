using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Interfaces.Repositories;

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
        IApiKeyRepository apiKeys,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        var keys = await apiKeys.ListByUserAsync(user.Id, ct);

        var response = keys
            .Select(k => new Response(
                k.Id, k.Name, k.KeyPrefix, k.ExpiresAt, k.RevokedAt, k.LastUsedAt, k.Scopes, k.CreatedAt))
            .ToList();

        return Results.Ok(response);
    }
}
