using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class GetLearning
{
    public record Response(
        Guid Id,
        LearningKind Kind,
        string Title,
        string Body,
        string? Recommendation,
        decimal Confidence,
        int OccurrenceCount,
        IReadOnlyList<string> AgentNames,
        IReadOnlyList<string> DomainTags,
        LearningStatus Status,
        string? RetractedBy,
        string? RetractedReason,
        Guid? SupersededById,
        PromotionStatus PromotionStatus,
        DateTime? PromotedAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (l.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);

        return Results.Ok(new Response(
            l.Id, l.Kind, l.Title, l.Body, l.Recommendation, l.Confidence, l.OccurrenceCount,
            l.AgentNames, l.DomainTags, l.Status, l.RetractedBy, l.RetractedReason,
            l.SupersededById, l.PromotionStatus, l.PromotedAt, l.CreatedAt));
    }
}
