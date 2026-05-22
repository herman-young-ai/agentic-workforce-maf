using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Milestones;

public static class GetMilestone
{
    public record Response(
        Guid Id,
        string Title,
        string Summary,
        string? WorkflowRunIds,
        string? KeyOutcomes,
        IReadOnlyList<string> DomainTags,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/milestones/{milestoneId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Milestones");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid milestoneId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IMilestoneRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var m = await repo.GetByIdAsync(milestoneId, ct)
            ?? throw new NotFoundException("Milestone", milestoneId);
        if (m.ProjectId != projectId)
            throw new NotFoundException("Milestone", milestoneId);

        return Results.Ok(new Response(
            m.Id, m.Title, m.Summary, m.WorkflowRunIds, m.KeyOutcomes,
            m.DomainTags, m.PeriodStart, m.PeriodEnd, m.CreatedAt));
    }
}
