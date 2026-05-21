using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var member = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct)
            ?? throw new NotFoundException("Member", userId);

        if (member.Role == ProjectRole.Owner)
            throw new BusinessRuleException("The project owner cannot be removed. Transfer ownership first.");

        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
