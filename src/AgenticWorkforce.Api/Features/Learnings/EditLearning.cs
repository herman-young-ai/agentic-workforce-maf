using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class EditLearning
{
    public record Request(string? Title, string? Body, string? Recommendation, decimal? Confidence);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Owner, ct);

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (l.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);
        if (l.Status != LearningStatus.Active)
            throw new InvalidStateException($"Only active learnings can be edited (current: {l.Status}).");

        if (request.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                throw new ValidationException("Title cannot be empty.");
            l.Title = request.Title;
        }
        if (request.Body is not null) l.Body = request.Body;
        if (request.Recommendation is not null) l.Recommendation = request.Recommendation;
        if (request.Confidence.HasValue)
        {
            if (request.Confidence.Value < 0 || request.Confidence.Value > 1)
                throw new ValidationException("Confidence must be between 0 and 1.");
            l.Confidence = request.Confidence.Value;
        }

        await repo.UpdateAsync(l, ct);
        return Results.NoContent();
    }
}
