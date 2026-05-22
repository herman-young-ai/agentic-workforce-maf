using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

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

        // Final state machine transition: PendingApproval -> Approved.
        // The repository sets PromotedBy + PromotedAt in the same transaction.
        await repo.ApprovePromotionAsync(learningId, user.Id, ct);
        return Results.NoContent();
    }
}
