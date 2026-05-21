using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Members;

public static class UpdateMember
{
    public record Request(ProjectRole Role);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}/members/{userId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Members")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid userId,
        [FromBody] Request request,
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

        // Cannot change the role of the owner without using TransferOwnership
        if (member.Role == ProjectRole.Owner)
            throw new BusinessRuleException("Use the transfer-ownership endpoint to change the project owner.");

        if (request.Role == ProjectRole.Owner)
            throw new BusinessRuleException("Use the transfer-ownership endpoint to assign a new owner.");

        member.Role = request.Role;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
