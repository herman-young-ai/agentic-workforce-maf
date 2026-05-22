using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Auth;

public static class RevokeApiKey
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/auth/me/api-keys/{keyId:guid}", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth")
            ;

    private static async Task<IResult> HandleAsync(
        Guid keyId,
        ICurrentUserAccessor userAccessor,
        IApiKeyRepository apiKeys,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        // Distinguish between "no such key for this user" (404) and
        // "key already revoked" (204 idempotent). The repository returns false
        // for both — query the row to disambiguate.
        var existing = await apiKeys.GetByIdForUserAsync(keyId, user.Id, ct)
            ?? throw new NotFoundException("ApiKey", keyId);

        if (existing.RevokedAt.HasValue)
            return Results.NoContent();

        await apiKeys.RevokeAsync(keyId, user.Id, ct);

        return Results.NoContent();
    }
}
