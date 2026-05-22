using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Services;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class RetryTask
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/retry", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid taskId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        // Endpoint-level constraint: Retry only accepts Failed source. The
        // lifecycle gate below is the matrix-owned invariant.
        if (task.Status != TaskStatus.Failed)
            throw new InvalidStateException($"Only failed tasks can be retried (current status: {task.Status}).");

        if (!TaskStateValidator.CanTransition(task.Status, TaskStatus.Approved))
            throw new InvalidStateException(
                $"Lifecycle forbids {task.Status} -> Approved transition.");

        if (task.RetryCount >= task.MaxRetries)
            throw new BusinessRuleException($"Task has reached its maximum retry limit ({task.MaxRetries}).");

        task.Status = TaskStatus.Approved;
        task.RetryCount += 1;
        await repo.UpdateAsync(task, ct);

        return Results.NoContent();
    }
}
