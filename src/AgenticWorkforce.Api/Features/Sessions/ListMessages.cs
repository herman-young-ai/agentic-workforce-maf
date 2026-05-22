using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;
using Microsoft.AspNetCore.Mvc;

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
        ISessionRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        if (!await repo.ExistsInProjectAsync(sessionId, projectId, ct))
            throw new NotFoundException("Session", sessionId);

        var page = await repo.ListMessagesPagedAsync(sessionId, paging, ct);

        var items = page.Items
            .Select(m => new Response(m.Id, m.Role, m.Content, m.SenderId, m.Model,
                m.InputTokens, m.OutputTokens, m.CostUsd, m.ToolName, m.CreatedAt))
            .ToList();

        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
