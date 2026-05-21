using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Members;

public static class TransferOwnership
{
    public record Request(Guid NewOwnerId);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/members/transfer-ownership", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Members")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        if (request.NewOwnerId == user.Id)
            throw new BusinessRuleException("You are already the owner of this project.");

        var currentOwner = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == user.Id, ct)
            ?? throw new NotFoundException("Member", user.Id);

        var newOwnerMember = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == request.NewOwnerId, ct)
            ?? throw new NotFoundException("Member", request.NewOwnerId);

        currentOwner.Role = ProjectRole.Operator;
        newOwnerMember.Role = ProjectRole.Owner;

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
