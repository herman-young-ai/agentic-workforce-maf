using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class ListSessions
{
    public record Response(
        Guid Id,
        SessionStatus Status,
        Guid? UserId,
        string? AgentName,
        string? Goal,
        decimal TotalCostUsd,
        DateTime? LastActivityAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/sessions", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Sessions")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        [FromQuery] SessionStatus? status,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ISessionRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var page = await repo.ListByProjectPagedAsync(projectId, status, paging, ct);

        var items = page.Items
            .Select(s => new Response(s.Id, s.Status, s.UserId, s.AgentName, s.Goal,
                s.TotalCostUsd, s.LastActivityAt, s.CreatedAt))
            .ToList();

        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
