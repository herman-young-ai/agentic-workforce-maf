using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Features.Projects;

public static class PauseProject
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/pause", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectRepository repo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var project = await repo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        if (project.Status != ProjectStatus.Active)
            throw new InvalidStateException($"Only active projects can be paused (current status: {project.Status}).");

        project.Status = ProjectStatus.Paused;

        await publisher.PublishAsync(
            ProjectEventBuilder.ForProject(
                projectId, EventTypes.ProjectPaused, user.Email,
                new { project.Name }),
            ct);

        await repo.UpdateAsync(project, ct);

        return Results.NoContent();
    }
}
