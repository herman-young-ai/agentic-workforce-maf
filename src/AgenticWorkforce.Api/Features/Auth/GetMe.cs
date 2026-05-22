using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Auth;

public static class GetMe
{
    public record Response(
        Guid Id,
        string Email,
        string DisplayName,
        SystemRole SystemRole,
        bool IsActive,
        IReadOnlyList<string> Roles,
        DateTime? LastLoginAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/auth/me", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth")
            ;

    private static async Task<IResult> HandleAsync(
        ICurrentUserAccessor userAccessor,
        IUserRepository users,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        var dbUser = await users.GetByIdAsync(user.Id, ct);
        if (dbUser is null)
            return Results.Ok(new Response(user.Id, user.Email, user.DisplayName, SystemRole.Member, true, user.Roles, null));

        return Results.Ok(new Response(
            dbUser.Id, dbUser.Email, dbUser.DisplayName, dbUser.SystemRole,
            dbUser.IsActive, user.Roles, dbUser.LastLoginAt));
    }
}
