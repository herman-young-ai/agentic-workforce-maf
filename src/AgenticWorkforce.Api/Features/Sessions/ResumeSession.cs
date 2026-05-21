using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class ResumeSession
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/sessions/{sessionId:guid}/resume", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Sessions")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid sessionId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ISessionRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var session = await repo.GetByIdAsync(sessionId, ct)
            ?? throw new NotFoundException("Session", sessionId);

        if (session.ProjectId != projectId)
            throw new NotFoundException("Session", sessionId);

        if (session.Status != SessionStatus.Suspended)
            throw new InvalidStateException($"Only suspended sessions can be resumed (current status: {session.Status}).");

        session.Status = SessionStatus.Active;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
