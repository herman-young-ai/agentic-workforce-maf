using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var members = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Include(m => m.User)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new Response(m.UserId, m.User.Email, m.User.DisplayName, m.Role, m.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(members);
    }
}
