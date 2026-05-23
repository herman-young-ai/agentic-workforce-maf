using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Services;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class CancelTask
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/cancel", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid taskId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        if (!TaskStateValidator.CanTransition(task.Status, TaskStatus.Cancelled))
            throw new InvalidStateException($"Tasks in status {task.Status} cannot be cancelled.");

        var previousStatus = task.Status;
        task.Status = TaskStatus.Cancelled;

        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            TaskId    = task.Id,
            EventType = EventTypes.TaskCancelled,
            Source    = user.Email,
            Severity  = EventSeverity.Info,
            Data      = JsonSerializer.Serialize(new { task.Id, task.Objective, PreviousStatus = previousStatus.ToString() })
        }, ct);

        await repo.UpdateAsync(task, ct);

        return Results.NoContent();
    }
}
