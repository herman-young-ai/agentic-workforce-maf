using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Milestones;

public static class ListMilestones
{
    public record Response(
        Guid Id,
        string Title,
        string Summary,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/milestones", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Milestones");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IMilestoneRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);
        var page = await repo.ListByProjectPagedAsync(projectId, paging, ct);
        var items = page.Items
            .Select(m => new Response(m.Id, m.Title, m.Summary, m.PeriodStart, m.PeriodEnd, m.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
