using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class ListPendingPromotions
{
    public record Response(
        Guid Id,
        Guid ProjectId,
        LearningKind Kind,
        string Title,
        decimal Confidence,
        Guid? PromotionRequestedById,
        DateTime? PromotionRequestedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/knowledge/promotions/pending", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        [AsParameters] PagedQuery paging,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var page = await repo.ListPendingPromotionsPagedAsync(paging, ct);
        var items = page.Items
            .Select(l => new Response(l.Id, l.ProjectId, l.Kind, l.Title, l.Confidence,
                l.PromotionRequestedById, l.PromotionRequestedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
