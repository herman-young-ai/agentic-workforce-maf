using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Projects;

public static class UpdateProject
{
    public record Request(
        string? Name,
        string? Objective,
        string? Description,
        decimal? BudgetCeilingUsd,
        string? Jurisdiction);

    public record Response(
        Guid Id,
        string Name,
        string Objective,
        string? Description,
        ProjectStatus Status,
        ProjectTier Tier,
        decimal? BudgetCeilingUsd,
        string? Jurisdiction,
        DateTime UpdatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/projects/{projectId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Projects")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectRepository repo,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var project = await repo.GetByIdAsync(projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        if (project.Status == ProjectStatus.Archived)
            throw new InvalidStateException("Archived projects cannot be modified.");

        if (request.Name is not null && request.Name != project.Name)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("Project name cannot be empty.");
            if (await repo.ExistsByNameAsync(request.Name, ct))
                throw new AlreadyExistsException("Project", $"name '{request.Name}'");
            project.Name = request.Name;
        }

        if (request.Objective is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Objective))
                throw new ValidationException("Project objective cannot be empty.");
            project.Objective = request.Objective;
        }

        if (request.Description is not null) project.Description = request.Description;
        if (request.BudgetCeilingUsd is not null) project.BudgetCeilingUsd = request.BudgetCeilingUsd;
        if (request.Jurisdiction is not null) project.Jurisdiction = request.Jurisdiction;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new Response(
            project.Id, project.Name, project.Objective, project.Description,
            project.Status, project.Tier, project.BudgetCeilingUsd, project.Jurisdiction,
            project.UpdatedAt));
    }
}
