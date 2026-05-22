using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

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
        ITaskRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        if (request.TaskIds.Count == 0)
            throw new ValidationException("At least one task ID is required.");

        var result = await repo.BulkApproveAsync(projectId, request.TaskIds, user.Id, ct);

        var responseItems = result.Items
            .Select(i => new ApprovalResult(i.TaskId, i.Approved, i.Reason))
            .ToList();

        return Results.Ok(new Response(responseItems));
    }
}
