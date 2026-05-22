using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Members;

public static class RemoveMember
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/members/{userId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Members")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid userId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectMemberRepository members,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var member = await members.GetMembershipAsync(userId, projectId, ct)
            ?? throw new NotFoundException("Member", userId);

        if (member.Role == ProjectRole.Owner)
            throw new BusinessRuleException("The project owner cannot be removed. Transfer ownership first.");

        if (!await members.RemoveAsync(projectId, userId, ct))
            throw new NotFoundException("Member", userId);

        return Results.NoContent();
    }
}
