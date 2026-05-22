using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class PromoteLearning
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}/promote", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (l.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);
        if (l.Status != LearningStatus.Active)
            throw new InvalidStateException($"Only active learnings can be promoted (current: {l.Status}).");
        if (l.PromotionStatus is PromotionStatus.PendingApproval or PromotionStatus.Approved)
            throw new InvalidStateException(
                $"Learning is already in promotion state {l.PromotionStatus}.");

        // State transition None|Rejected -> PendingApproval. Final approval is
        // gated by §4.18 ApprovePromotion (PlatformAdmin) per the plan's
        // promotion state machine.
        await repo.RequestPromotionAsync(learningId, user.Id, ct);
        return Results.NoContent();
    }
}
