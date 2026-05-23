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

public static class ApproveTask
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/approve", HandleAsync)
            .RequireAuthorization(Policies.RequireReviewer)
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
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        // Endpoint-level constraint: the Approve endpoint accepts only the
        // Proposed source state. Failed-source approval flows through Retry
        // (which also bumps RetryCount). The lifecycle gate below is the
        // system-level invariant owned by TaskStateValidator.
        if (task.Status != TaskStatus.Proposed)
            throw new InvalidStateException($"Only proposed tasks can be approved (current status: {task.Status}).");

        if (!TaskStateValidator.CanTransition(task.Status, TaskStatus.Approved))
            throw new InvalidStateException(
                $"Lifecycle forbids {task.Status} -> Approved transition.");

        SegregationOfDuties.Enforce(task.CreatedById, user.Id, "approve their own task");

        task.Status = TaskStatus.Approved;

        // Publish BEFORE the repo write so the event row is included in
        // the same SaveChanges transaction as the status change. The post-
        // commit interceptor then fans out to Redis pub/sub.
        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            TaskId    = task.Id,
            EventType = EventTypes.TaskApproved,
            Source    = user.Email,
            Severity  = EventSeverity.Info,
            Data      = JsonSerializer.Serialize(new { task.Id, task.Objective })
        }, ct);

        await repo.UpdateAsync(task, ct);

        return Results.NoContent();
    }
}
