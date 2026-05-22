using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.HumanInput;

public static class GetAudit
{
    public record Response(
        Guid Id,
        Guid WorkflowRunId,
        Guid TaskId,
        HumanInputRequestStatus Status,
        HumanDecisionType? Decision,
        string? ResponseText,
        Guid? ResponderId,
        Guid? TriggeredById,
        DateTime CreatedAt,
        DateTime? ResolvedAt,
        DateTime? TimeoutAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/human-input/{requestId:guid}/audit", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("HumanInput");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid requestId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IHumanInputRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var h = await repo.GetByIdAsync(requestId, ct)
            ?? throw new NotFoundException("HumanInputRequest", requestId);
        if (h.ProjectId != projectId)
            throw new NotFoundException("HumanInputRequest", requestId);

        return Results.Ok(new Response(
            h.Id, h.WorkflowRunId, h.TaskId, h.Status, h.Decision, h.Response,
            h.ResponderId, h.WorkflowRun.TriggeredById,
            h.CreatedAt, h.ResolvedAt, h.TimeoutAt));
    }
}
