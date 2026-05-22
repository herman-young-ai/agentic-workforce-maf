using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class EditPlatformLearning
{
    public record Request(string? Title, string? Body, string? Recommendation, decimal? Confidence);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/admin/knowledge/learnings/{learningId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        Guid learningId,
        [FromBody] Request request,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);

        if (l.PromotionStatus != PromotionStatus.Approved)
            throw new InvalidStateException(
                "Only platform-approved learnings can be edited at the admin level.");

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
