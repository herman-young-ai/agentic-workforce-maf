using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Pagination;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class ListMessages
{
    public record Response(
        Guid Id,
        MessageRole Role,
        string? Content,
        string? SenderId,
        string? Model,
        long InputTokens,
        long OutputTokens,
        decimal CostUsd,
        string? ToolName,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/sessions/{sessionId:guid}/messages", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Sessions")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid sessionId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var sessionExists = await db.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.ProjectId == projectId, ct);

        if (!sessionExists)
            throw new NotFoundException("Session", sessionId);

        var result = await db.SessionMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new Response(m.Id, m.Role, m.Content, m.SenderId, m.Model,
                m.InputTokens, m.OutputTokens, m.CostUsd, m.ToolName, m.CreatedAt))
            .ToPagedResultAsync(paging, ct);

        return Results.Ok(result);
    }
}
