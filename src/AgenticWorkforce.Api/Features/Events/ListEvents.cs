using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Events;

public static class ListEvents
{
    public record Response(
        Guid Id,
        Guid? TaskId,
        Guid? SessionId,
        string EventType,
        string? Source,
        string? Data,
        EventSeverity Severity,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/events", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Events");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        [FromQuery] EventSeverity? minSeverity,
        [FromQuery] string? eventType,
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? sessionId,
        [FromQuery] DateTime? since,
        [FromQuery] DateTime? until,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IEventRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var filter = new EventFilter(minSeverity, eventType, taskId, sessionId, since, until);
        var page = await repo.ListByProjectPagedAsync(projectId, filter, paging, ct);

        var items = page.Items
            .Select(e => new Response(e.Id, e.TaskId, e.SessionId, e.EventType, e.Source,
                e.Data, e.Severity, e.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
