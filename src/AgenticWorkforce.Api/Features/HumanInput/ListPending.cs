using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.HumanInput;

public static class ListPending
{
    public record Response(
        Guid Id,
        Guid WorkflowRunId,
        Guid TaskId,
        string PromptMessage,
        string? Channel,
        string? Choices,
        DateTime? TimeoutAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/human-input/pending", HandleAsync)
            .RequireAuthorization(Policies.RequireReviewer)
            .WithTags("HumanInput");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IHumanInputRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Reviewer, ct);

        var list = await repo.ListPendingByProjectAsync(projectId, ct);
        return Results.Ok(list.Select(h => new Response(
            h.Id, h.WorkflowRunId, h.TaskId, h.PromptMessage, h.Channel, h.Choices,
            h.TimeoutAt, h.CreatedAt)).ToList());
    }
}
