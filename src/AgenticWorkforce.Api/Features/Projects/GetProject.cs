using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Projects;

public static class GetProject
{
    public record MemberSummary(Guid UserId, ProjectRole Role);

    public record Response(
        Guid Id,
        string Name,
        string Objective,
        string? Description,
        ProjectStatus Status,
        ProjectTier Tier,
        decimal? BudgetCeilingUsd,
        string? Jurisdiction,
        int MemberCount,
        int TotalTaskCount,
        int ActiveTaskCount,
        IReadOnlyList<MemberSummary> Members,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private static readonly TaskStatus[] ActiveTaskStatuses =
        [TaskStatus.Approved, TaskStatus.Queued, TaskStatus.Running];

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectRepository projects,
        ITaskRepository tasks,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var project = await projects.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var totalTaskCount = await tasks.CountByProjectAsync(projectId, statuses: null, ct);
        var activeTaskCount = await tasks.CountByProjectAsync(projectId, ActiveTaskStatuses, ct);

        var members = project.Members
            .Select(m => new MemberSummary(m.UserId, m.Role))
            .ToList();

        return Results.Ok(new Response(
            project.Id, project.Name, project.Objective, project.Description,
            project.Status, project.Tier, project.BudgetCeilingUsd, project.Jurisdiction,
            members.Count, totalTaskCount, activeTaskCount,
            members, project.CreatedAt, project.UpdatedAt));
    }
}
