using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class BulkApproveTask
{
    public record Request(IReadOnlyList<Guid> TaskIds);

    public record ApprovalResult(Guid TaskId, bool Approved, string? Reason);

    public record Response(IReadOnlyList<ApprovalResult> Results);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/tasks/bulk-approve", HandleAsync)
            .RequireAuthorization(Policies.RequireReviewer)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        if (request.TaskIds.Count == 0)
            throw new ValidationException("At least one task ID is required.");

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId && request.TaskIds.Contains(t.Id))
            .ToListAsync(ct);

        var results = new List<ApprovalResult>();

        foreach (var task in tasks)
        {
            if (task.Status != TaskStatus.Proposed)
            {
                results.Add(new ApprovalResult(task.Id, false, $"Task is not in Proposed status (current: {task.Status})."));
                continue;
            }

            if (task.CreatedById.HasValue && task.CreatedById == user.Id)
            {
                results.Add(new ApprovalResult(task.Id, false, "Segregation of duties: creator cannot approve their own task."));
                continue;
            }

            task.Status = TaskStatus.Approved;
            results.Add(new ApprovalResult(task.Id, true, null));
        }

        foreach (var missingId in request.TaskIds.Except(tasks.Select(t => t.Id)))
            results.Add(new ApprovalResult(missingId, false, "Task not found in this project."));

        await db.SaveChangesAsync(ct);

        return Results.Ok(new Response(results));
    }
}
