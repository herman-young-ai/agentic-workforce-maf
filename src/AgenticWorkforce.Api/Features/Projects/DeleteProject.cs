using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

namespace AgenticWorkforce.Api.Features.Projects;

public static class DeleteProject
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/v1/projects/{projectId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var project = await repo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        project.Status = ProjectStatus.Archived;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
