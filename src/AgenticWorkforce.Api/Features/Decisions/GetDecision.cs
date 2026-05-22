using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Decisions;

public static class GetDecision
{
    public record Response(
        Guid Id,
        string DecisionRef,
        string Domain,
        string Decision,
        string Rationale,
        string MadeBy,
        DecisionStatus Status,
        Guid? SupersededById,
        Guid? TaskId,
        Guid? WorkflowRunId,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/decisions/{decisionId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Decisions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid decisionId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDecisionRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var d = await repo.GetByIdAsync(decisionId, ct)
            ?? throw new NotFoundException("Decision", decisionId);
        if (d.ProjectId != projectId)
            throw new NotFoundException("Decision", decisionId);

        return Results.Ok(new Response(
            d.Id, d.DecisionRef, d.Domain, d.Decision, d.Rationale, d.MadeBy,
            d.Status, d.SupersededById, d.TaskId, d.WorkflowRunId, d.CreatedAt));
    }
}
