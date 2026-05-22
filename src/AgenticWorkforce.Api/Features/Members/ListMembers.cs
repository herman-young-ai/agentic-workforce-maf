using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Members;

public static class ListMembers
{
    public record Response(
        Guid UserId,
        string Email,
        string DisplayName,
        ProjectRole Role,
        DateTime JoinedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/members", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Members")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectMemberRepository members,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var list = await members.ListByProjectAsync(projectId, ct);

        var response = list
            .Select(m => new Response(m.UserId, m.User.Email, m.User.DisplayName, m.Role, m.CreatedAt))
            .ToList();

        return Results.Ok(response);
    }
}
