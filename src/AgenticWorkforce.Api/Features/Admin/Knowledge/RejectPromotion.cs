using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class RejectPromotion
{
    public record Request(string Reason);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/knowledge/learnings/{learningId:guid}/reject-promotion", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        Guid learningId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        ILearningRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Reason is required for rejection.");

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);

        if (l.PromotionStatus != PromotionStatus.PendingApproval)
            throw new InvalidStateException(
                $"Only learnings in PendingApproval can be rejected (current: {l.PromotionStatus}).");

        // Principle 11: requester cannot reject their own request either —
        // closing a request (positive or negative) is a peer-review act.
        SegregationOfDuties.Enforce(
            l.PromotionRequestedById, userAccessor.User.Id, "reject their own promotion request");

        await repo.RejectPromotionAsync(learningId, request.Reason, ct);
        return Results.NoContent();
    }
}
