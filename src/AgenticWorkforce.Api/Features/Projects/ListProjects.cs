using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Projects;

public static class ListProjects
{
    public record Response(
        Guid Id,
        string Name,
        string Objective,
        ProjectStatus Status,
        ProjectTier Tier,
        decimal? BudgetCeilingUsd,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        [AsParameters] PagedQuery paging,
        [FromQuery] ProjectStatus? status,
        ICurrentUserAccessor userAccessor,
        IProjectRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        var page = await repo.ListByMemberPagedAsync(user.Id, status, paging, ct);

        var items = page.Items
            .Select(p => new Response(p.Id, p.Name, p.Objective, p.Status, p.Tier, p.BudgetCeilingUsd, p.CreatedAt))
            .ToList();

        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
