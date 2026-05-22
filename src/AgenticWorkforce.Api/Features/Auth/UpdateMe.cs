using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

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
        IUserRepository users,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ValidationException("Display name cannot be empty.");

        var user = userAccessor.User;

        var dbUser = await users.GetByIdAsync(user.Id, ct)
            ?? throw new NotFoundException("User", user.Id);

        dbUser.DisplayName = request.DisplayName;
        await users.UpdateAsync(dbUser, ct);

        return Results.NoContent();
    }
}
