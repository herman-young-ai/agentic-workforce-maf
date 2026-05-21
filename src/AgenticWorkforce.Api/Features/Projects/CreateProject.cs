using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Projects;

public static class CreateProject
{
    public record Request(
        string Name,
        string Objective,
        string? Description,
        decimal? BudgetCeilingUsd,
        string? Jurisdiction,
        ProjectTier Tier = ProjectTier.User);

    public record Response(
        Guid Id,
        string Name,
        string Objective,
        ProjectStatus Status,
        ProjectTier Tier,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects", HandleAsync)
            .RequireAuthorization()
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectRepository repo,
        IIdempotencyService idempotency,
        AppDbContext db,
        CancellationToken ct)
    {
        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{cached.Id}", cached);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Project name is required.");

        if (string.IsNullOrWhiteSpace(request.Objective))
            throw new ValidationException("Project objective is required.");

        if (await repo.ExistsByNameAsync(request.Name, ct))
            throw new AlreadyExistsException("Project", $"name '{request.Name}'");

        var user = userAccessor.User;
        var project = new Project
        {
            Name        = request.Name,
            Objective   = request.Objective,
            Description = request.Description,
            BudgetCeilingUsd = request.BudgetCeilingUsd,
            Jurisdiction = request.Jurisdiction,
            Tier        = request.Tier
        };

        var ownerMember = new ProjectMember
        {
            Project = project,
            UserId  = user.Id,
            Role    = ProjectRole.Owner
        };

        db.Projects.Add(project);
        db.ProjectMembers.Add(ownerMember);
        await db.SaveChangesAsync(ct);

        var response = new Response(project.Id, project.Name, project.Objective, project.Status, project.Tier, project.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{project.Id}", response);
    }
}
