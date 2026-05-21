using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Pagination;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
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
        var projects = await repo.ListByMemberAsync(user.Id, ct);

        var filtered = status.HasValue
            ? projects.Where(p => p.Status == status.Value).ToList()
            : projects.ToList();

        var page = filtered
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(p => new Response(p.Id, p.Name, p.Objective, p.Status, p.Tier, p.BudgetCeilingUsd, p.CreatedAt))
            .ToList();

        return Results.Ok(new PagedResult<Response>(page, paging.Page, paging.PageSize, filtered.Count));
    }
}
