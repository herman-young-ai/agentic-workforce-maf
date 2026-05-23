using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class RejectTask
{
    public record Request(string Reason);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/reject", HandleAsync)
            .RequireAuthorization(Policies.RequireReviewer)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid taskId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Rejection reason is required.");

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        // Endpoint-level constraint: Reject is a Proposed-only gate. Other
        // sources cancel via /cancel. The lifecycle gate below is matrix-owned.
        if (task.Status != TaskStatus.Proposed)
            throw new InvalidStateException($"Only proposed tasks can be rejected (current status: {task.Status}).");

        if (!TaskStateValidator.CanTransition(task.Status, TaskStatus.Cancelled))
            throw new InvalidStateException(
                $"Lifecycle forbids {task.Status} -> Cancelled transition.");

        task.Status = TaskStatus.Cancelled;
        task.OutputSummary = $"Rejected: {request.Reason}";

        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            TaskId    = task.Id,
            EventType = EventTypes.TaskRejected,
            Source    = user.Email,
            Severity  = EventSeverity.Info,
            Data      = JsonSerializer.Serialize(new { task.Id, task.Objective, request.Reason })
        }, ct);

        await repo.UpdateAsync(task, ct);

        return Results.NoContent();
    }
}
