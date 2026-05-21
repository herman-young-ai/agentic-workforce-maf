using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

namespace AgenticWorkforce.Api.Features.Projects;

public static class ResumeProject
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/resume", HandleAsync)
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

        if (project.Status != ProjectStatus.Paused)
            throw new InvalidStateException($"Only paused projects can be resumed (current status: {project.Status}).");

        project.Status = ProjectStatus.Active;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
