using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Queries;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Executions;

public static class DispatchTasks
{
    public record Request(IReadOnlyList<Guid> TaskIds);
    public record Response(Guid ExecutionId, ExecutionState State);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/executions/dispatch", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Executions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IExecutionRepository executions,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (request.TaskIds.Count == 0)
            throw new ValidationException("At least one task ID is required.");

        // Api enqueues only. Worker (Phase 8) consumes the message and creates
        // the WorkflowRun row — Principle 16 (single source of truth).
        var executionId = await executions.EnqueueDispatchAsync(projectId, request.TaskIds, user.Id, ct);

        return Results.Accepted(
            $"/api/v1/projects/{projectId}/executions/{executionId}",
            new Response(executionId, ExecutionState.Pending));
    }
}
