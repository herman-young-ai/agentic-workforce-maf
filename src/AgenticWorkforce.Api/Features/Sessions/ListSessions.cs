using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Pagination;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var query = db.Sessions
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId);

        if (status.HasValue) query = query.Where(s => s.Status == status.Value);

        var result = await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new Response(s.Id, s.Status, s.UserId, s.AgentName, s.Goal,
                s.TotalCostUsd, s.LastActivityAt, s.CreatedAt))
            .ToPagedResultAsync(paging, ct);

        return Results.Ok(result);
    }
}
