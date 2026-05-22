using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Executions;

public static class RunAdHoc
{
    public record Request(string Objective);
    public record Response(Guid ExecutionId, ExecutionState State);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/executions/run", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Executions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIdempotencyService idempotency,
        IExecutionRepository executions,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(user.Id, idempotencyKey, ct);
            if (cached is not null)
                return Results.Accepted(
                    $"/api/v1/projects/{projectId}/executions/{cached.ExecutionId}", cached);
        }

        if (string.IsNullOrWhiteSpace(request.Objective))
            throw new ValidationException("Objective is required.");

        var executionId = await executions.EnqueueAdHocAsync(projectId, request.Objective, user.Id, ct);
        var response = new Response(executionId, ExecutionState.Pending);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(user.Id, idempotencyKey, response, ct);

        return Results.Accepted(
            $"/api/v1/projects/{projectId}/executions/{executionId}", response);
    }
}
