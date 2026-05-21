using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Team;

public static class SeedTeam
{
    public record AgentAdded(Guid Id, string AgentName, AgentRole Role);

    public record Response(int Added, IReadOnlyList<AgentAdded> Agents);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/team/seed", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
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
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var allCatalogAgents = await db.AgentCatalogs
            .AsNoTracking()
            .Where(a => a.Enabled)
            .OrderBy(a => a.AgentName)
            .ToListAsync(ct);

        var existingAgentCatalogIds = await db.ProjectAgents
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.AgentCatalogId)
            .ToListAsync(ct);

        var toAdd = allCatalogAgents
            .Where(c => !existingAgentCatalogIds.Contains(c.Id))
            .ToList();

        if (toAdd.Count == 0)
            return Results.Ok(new Response(0, []));

        var displayOrder = existingAgentCatalogIds.Count;
        var added = new List<AgentAdded>();

        foreach (var catalog in toAdd)
        {
            var agent = new ProjectAgent
            {
                ProjectId      = projectId,
                AgentCatalogId = catalog.Id,
                Role           = AgentRole.Specialist,
                Enabled        = true,
                DisplayOrder   = ++displayOrder
            };
            db.ProjectAgents.Add(agent);
            added.Add(new AgentAdded(agent.Id, catalog.AgentName, agent.Role));
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new Response(added.Count, added));
    }
}
