using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;

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
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var project = await repo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        if (project.Status != ProjectStatus.Paused)
            throw new InvalidStateException($"Only paused projects can be resumed (current status: {project.Status}).");

        project.Status = ProjectStatus.Active;

        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            EventType = EventTypes.ProjectResumed,
            Source    = user.Email,
            Severity  = EventSeverity.Info,
            Data      = JsonSerializer.Serialize(new { project.Id, project.Name })
        }, ct);

        await repo.UpdateAsync(project, ct);

        return Results.NoContent();
    }
}
