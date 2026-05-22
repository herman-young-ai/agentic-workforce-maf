using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class ListTasks
{
    public record Response(
        Guid Id,
        TaskType Type,
        TaskStatus Status,
        string Objective,
        string? AgentName,
        TaskSource Source,
        decimal CostUsd,
        Guid? ParentTaskId,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/tasks", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        [FromQuery] TaskStatus? status,
        [FromQuery] TaskType? type,
        [FromQuery] TaskSource? source,
        [FromQuery] string? agentName,
        [FromQuery] Guid? parentTaskId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var filter = new TaskListFilter(status, type, source, agentName, parentTaskId);
        var page = await repo.ListByProjectPagedAsync(projectId, filter, paging, ct);

        var items = page.Items
            .Select(t => new Response(t.Id, t.Type, t.Status, t.Objective, t.AgentName, t.Source, t.CostUsd, t.ParentTaskId, t.CreatedAt))
            .ToList();

        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
