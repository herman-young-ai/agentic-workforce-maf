using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/team/{cached.Id}", cached);
        }

        var catalog = await db.AgentCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AgentCatalogId && a.Enabled, ct)
            ?? throw new NotFoundException("AgentCatalog", request.AgentCatalogId);

        var alreadyAdded = await db.ProjectAgents
            .AnyAsync(a => a.ProjectId == projectId && a.AgentCatalogId == request.AgentCatalogId, ct);

        if (alreadyAdded)
            throw new AlreadyExistsException("ProjectAgent", $"Agent '{catalog.AgentName}' is already on this project team.");

        var displayOrder = await db.ProjectAgents
            .Where(a => a.ProjectId == projectId)
            .Select(a => (int?)a.DisplayOrder)
            .MaxAsync(ct) ?? 0;

        var agent = new ProjectAgent
        {
            ProjectId      = projectId,
            AgentCatalogId = request.AgentCatalogId,
            Role           = request.Role,
            UserPrompt     = request.UserPrompt,
            Enabled        = true,
            DisplayOrder   = displayOrder + 1
        };

        db.ProjectAgents.Add(agent);
        await db.SaveChangesAsync(ct);

        var response = new Response(agent.Id, agent.AgentCatalogId, catalog.AgentName, agent.Role, agent.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/team/{agent.Id}", response);
    }
}
