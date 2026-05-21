using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;

namespace AgenticWorkforce.Api.Features.Sessions;

public static class CompleteSession
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/sessions/{sessionId:guid}/complete", HandleAsync)
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

        var completable = new[] { SessionStatus.Active, SessionStatus.Suspended };
        if (!completable.Contains(session.Status))
            throw new InvalidStateException($"Sessions in status {session.Status} cannot be completed.");

        session.Status = SessionStatus.Completed;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
