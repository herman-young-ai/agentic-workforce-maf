using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.HumanInput;

public static class Respond
{
    public record Request(HumanDecisionType Decision, string? Response);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/human-input/{requestId:guid}/respond", HandleAsync)
            .RequireAuthorization(Policies.RequireReviewer)
            .WithTags("HumanInput");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid requestId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IHumanInputRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Reviewer, ct);

        var existing = await repo.GetByIdAsync(requestId, ct)
            ?? throw new NotFoundException("HumanInputRequest", requestId);
        if (existing.ProjectId != projectId)
            throw new NotFoundException("HumanInputRequest", requestId);

        // Principle 11 — Segregation of Duties is enforced inside the repository
        // method against WorkflowRun.TriggeredById (Guid FK added in Phase 3.5).
        // The string TriggeredBy field is not used for this comparison.
        var outcome = await repo.RespondAsync(requestId, request.Decision, request.Response, user.Id, ct);

        if (outcome.Forbidden)
            throw new ForbiddenException(outcome.Reason ?? "Segregation of duties violation.");

        if (!outcome.Resolved)
            throw new InvalidStateException(outcome.Reason ?? "Request could not be resolved.");

        return Results.NoContent();
    }
}
