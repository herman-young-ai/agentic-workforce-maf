using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Auth;

public static class UpdateMe
{
    public record Request(string DisplayName);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/auth/me", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth")
            ;

    private static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ValidationException("Display name cannot be empty.");

        var user = userAccessor.User;

        var dbUser = await db.Users
            .FirstOrDefaultAsync(u => u.Id == user.Id, ct)
            ?? throw new NotFoundException("User", user.Id);

        dbUser.DisplayName = request.DisplayName;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
