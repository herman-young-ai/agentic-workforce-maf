using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Team;

public static class ListTeam
{
    public record Response(
        Guid Id,
        Guid AgentCatalogId,
        string AgentName,
        AgentRole Role,
        string? UserPrompt,
        bool Enabled,
        int DisplayOrder);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/team", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var team = await db.ProjectAgents
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Include(a => a.AgentCatalog)
            .OrderBy(a => a.DisplayOrder)
            .Select(a => new Response(a.Id, a.AgentCatalogId, a.AgentCatalog.AgentName, a.Role, a.UserPrompt, a.Enabled, a.DisplayOrder))
            .ToListAsync(ct);

        return Results.Ok(team);
    }
}
