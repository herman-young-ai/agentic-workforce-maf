using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.Catalog;

public static class ListCatalog
{
    public record Response(
        Guid Id,
        string AgentName,
        string? AgentType,
        string? Description,
        string[] Keywords,
        AgentVisibility Visibility,
        bool ChatEnabled);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/catalog", HandleAsync)
            .RequireAuthorization()
            .WithTags("Catalog");

    private static async Task<IResult> HandleAsync(
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        ICatalogQueryRepository repo,
        CancellationToken ct)
    {
        var isAdmin = userAccessor.User.IsInRole(Roles.PlatformAdmin);
        var page = await repo.ListVisibleAsync(isAdmin, paging, ct);

        var items = page.Items
            .Select(a => new Response(a.Id, a.AgentName, a.AgentType, a.Description,
                a.Keywords, a.Visibility, a.ChatEnabled))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
