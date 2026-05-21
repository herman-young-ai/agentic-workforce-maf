using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        var task = await repo.GetByIdAsync(taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        if (task.ProjectId != projectId)
            throw new NotFoundException("Task", taskId);

        if (task.Status != TaskStatus.Proposed)
            throw new InvalidStateException($"Only proposed tasks can be approved (current status: {task.Status}).");

        // Segregation of duties: the person who created the task cannot approve it
        if (task.CreatedById.HasValue && task.CreatedById == user.Id)
            throw new ForbiddenException("Segregation of duties: the task creator cannot approve their own task.");

        task.Status = TaskStatus.Approved;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
