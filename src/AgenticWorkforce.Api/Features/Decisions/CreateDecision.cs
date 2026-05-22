using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Decisions;

public static class CreateDecision
{
    public record Request(
        string DecisionRef,
        string Domain,
        string Decision,
        string Rationale,
        Guid? TaskId = null,
        Guid? WorkflowRunId = null);

    public record Response(Guid Id, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/decisions", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Decisions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDecisionRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.DecisionRef))
            throw new ValidationException("DecisionRef is required.");
        if (string.IsNullOrWhiteSpace(request.Decision))
            throw new ValidationException("Decision text is required.");

        var decision = await repo.AddAsync(new ProjectDecision
        {
            ProjectId     = projectId,
            DecisionRef   = request.DecisionRef,
            Domain        = request.Domain,
            Decision      = request.Decision,
            Rationale     = request.Rationale,
            MadeBy        = user.Email,
            Status        = DecisionStatus.Active,
            TaskId        = request.TaskId,
            WorkflowRunId = request.WorkflowRunId
        }, ct);

        return Results.Created(
            $"/api/v1/projects/{projectId}/decisions/{decision.Id}",
            new Response(decision.Id, decision.CreatedAt));
    }
}
