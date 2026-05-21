using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var project = await repo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var totalTaskCount = await db.Tasks
            .AsNoTracking()
            .CountAsync(t => t.ProjectId == projectId, ct);

        var activeTaskCount = await db.Tasks
            .AsNoTracking()
            .CountAsync(t => t.ProjectId == projectId &&
                             (t.Status == TaskStatus.Approved || t.Status == TaskStatus.Queued || t.Status == TaskStatus.Running), ct);

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
