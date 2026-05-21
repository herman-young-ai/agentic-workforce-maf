using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Pagination;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var query = db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId);

        if (status.HasValue) query = query.Where(t => t.Status == status.Value);
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);
        if (source.HasValue) query = query.Where(t => t.Source == source.Value);
        if (agentName is not null) query = query.Where(t => t.AgentName == agentName);
        if (parentTaskId.HasValue) query = query.Where(t => t.ParentTaskId == parentTaskId.Value);

        var result = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new Response(t.Id, t.Type, t.Status, t.Objective, t.AgentName, t.Source, t.CostUsd, t.ParentTaskId, t.CreatedAt))
            .ToPagedResultAsync(paging, ct);

        return Results.Ok(result);
    }
}
