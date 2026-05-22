using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class ListPlatformLearnings
{
    public record Response(
        Guid Id,
        Guid ProjectId,
        LearningKind Kind,
        string Title,
        decimal Confidence,
        DateTime? PromotedAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/knowledge/learnings", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        [AsParameters] PagedQuery paging,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var page = await repo.ListApprovedAcrossProjectsPagedAsync(paging, ct);
        var items = page.Items
            .Select(l => new Response(l.Id, l.ProjectId, l.Kind, l.Title, l.Confidence,
                l.PromotedAt, l.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
