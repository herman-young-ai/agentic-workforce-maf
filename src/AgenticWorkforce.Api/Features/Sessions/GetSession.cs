using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class GetSession
{
    public record Response(
        Guid Id,
        Guid ProjectId,
        SessionStatus Status,
        Guid? UserId,
        string? AgentName,
        string? Goal,
        string? RollingSummary,
        long TotalInputTokens,
        long TotalOutputTokens,
        decimal TotalCostUsd,
        decimal? CostBudgetUsd,
        int MessageCount,
        DateTime? LastActivityAt,
        DateTime? ExpiresAt,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/sessions/{sessionId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Sessions")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid sessionId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ISessionRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var session = await repo.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        if (session.ProjectId != projectId)
            throw new NotFoundException("Session", sessionId);

        return Results.Ok(new Response(
            session.Id, session.ProjectId, session.Status, session.UserId, session.AgentName,
            session.Goal, session.RollingSummary, session.TotalInputTokens, session.TotalOutputTokens,
            session.TotalCostUsd, session.CostBudgetUsd, session.Messages.Count,
            session.LastActivityAt, session.ExpiresAt, session.CreatedAt, session.UpdatedAt));
    }
}
