using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        var apiKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == user.Id, ct)
            ?? throw new NotFoundException("ApiKey", keyId);

        if (apiKey.RevokedAt.HasValue)
            return Results.NoContent();

        apiKey.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
