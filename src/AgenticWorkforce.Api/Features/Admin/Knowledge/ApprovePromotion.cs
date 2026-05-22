using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Services;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class ApprovePromotion
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/knowledge/learnings/{learningId:guid}/approve-promotion", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        Guid learningId,
        ICurrentUserAccessor userAccessor,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);

        if (l.PromotionStatus != PromotionStatus.PendingApproval)
            throw new InvalidStateException(
                $"Only learnings in PendingApproval can be approved (current: {l.PromotionStatus}).");

        // Principle 11: a PlatformAdmin who also requested the promotion
        // (e.g. holds project-level Operator + platform-level PlatformAdmin)
        // cannot approve their own request.
        SegregationOfDuties.Enforce(
            l.PromotionRequestedById, user.Id, "approve their own promotion request");

        // Final state machine transition: PendingApproval -> Approved.
        // The repository sets PromotedBy + PromotedAt in the same transaction.
        await repo.ApprovePromotionAsync(learningId, user.Id, ct);
        return Results.NoContent();
    }
}
