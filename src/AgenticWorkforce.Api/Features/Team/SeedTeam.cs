using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;

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
        IProjectAgentRepository projectAgents,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var seeded = await projectAgents.SeedFromCatalogAsync(projectId, ct);

        // SeedFromCatalogAsync defaults every new ProjectAgent to AgentRole.Specialist —
        // surface that explicitly rather than re-reading the row.
        var agents = seeded
            .Select(s => new AgentAdded(s.ProjectAgentId, s.AgentName, AgentRole.Specialist))
            .ToList();

        return Results.Ok(new Response(agents.Count, agents));
    }
}
