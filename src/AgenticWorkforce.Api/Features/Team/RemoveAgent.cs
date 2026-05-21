using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Team;

public static class RemoveAgent
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}/team/{memberId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid memberId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var agent = await db.ProjectAgents
            .FirstOrDefaultAsync(a => a.Id == memberId && a.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectAgent", memberId);

        db.ProjectAgents.Remove(agent);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
