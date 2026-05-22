using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Team;

public static class AddAgent
{
    public record Request(Guid AgentCatalogId, AgentRole Role = AgentRole.Specialist, string? UserPrompt = null);

    public record Response(Guid Id, Guid AgentCatalogId, string AgentName, AgentRole Role, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/team", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIdempotencyService idempotency,
        IAgentCatalogRepository catalog,
        IProjectAgentRepository projectAgents,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(user.Id, idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/team/{cached.Id}", cached);
        }

        var catalogEntry = await catalog.GetByIdAsync(request.AgentCatalogId, ct);
        if (catalogEntry is null || !catalogEntry.Enabled)
            throw new NotFoundException("AgentCatalog", request.AgentCatalogId);

        var existing = await projectAgents.ListByProjectAsync(projectId, ct);
        if (existing.Any(a => a.AgentCatalogId == request.AgentCatalogId))
            throw new AlreadyExistsException("ProjectAgent",
                $"Agent '{catalogEntry.AgentName}' is already on this project team.");

        var maxDisplayOrder = existing.Count == 0 ? 0 : existing.Max(a => a.DisplayOrder);

        var agent = await projectAgents.AddAsync(new ProjectAgent
        {
            ProjectId      = projectId,
            AgentCatalogId = request.AgentCatalogId,
            Role           = request.Role,
            UserPrompt     = request.UserPrompt,
            Enabled        = true,
            DisplayOrder   = maxDisplayOrder + 1
        }, ct);

        var response = new Response(agent.Id, agent.AgentCatalogId, catalogEntry.AgentName, agent.Role, agent.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(user.Id, idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/team/{agent.Id}", response);
    }
}
