using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class ListLearnings
{
    public record Response(
        Guid Id,
        LearningKind Kind,
        string Title,
        decimal Confidence,
        int OccurrenceCount,
        LearningStatus Status,
        PromotionStatus PromotionStatus,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/learnings", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var page = await repo.ListByProjectPagedAsync(projectId, paging, ct);
        var items = page.Items
            .Select(l => new Response(l.Id, l.Kind, l.Title, l.Confidence, l.OccurrenceCount,
                l.Status, l.PromotionStatus, l.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
