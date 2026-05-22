using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Decisions;

public static class ListDecisions
{
    public record Response(
        Guid Id,
        string DecisionRef,
        string Domain,
        string Decision,
        string MadeBy,
        DecisionStatus Status,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/decisions", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Decisions");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDecisionRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);
        var page = await repo.ListByProjectPagedAsync(projectId, paging, ct);
        var items = page.Items
            .Select(d => new Response(d.Id, d.DecisionRef, d.Domain, d.Decision, d.MadeBy, d.Status, d.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
