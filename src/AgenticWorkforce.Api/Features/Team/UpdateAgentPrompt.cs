using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Team;

public static class UpdateAgentPrompt
{
    public record Request(string UserPrompt, string? ChangeReason = null);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPut("/api/v1/projects/{projectId:guid}/team/{memberId:guid}/prompt", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Team")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid memberId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            throw new ValidationException("User prompt cannot be empty.");

        var agent = await db.ProjectAgents
            .FirstOrDefaultAsync(a => a.Id == memberId && a.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectAgent", memberId);

        var latestVersion = await db.PromptVersions
            .AsNoTracking()
            .Where(p => p.EntityType == "ProjectAgent" && p.EntityId == memberId)
            .Select(p => (int?)p.Version)
            .MaxAsync(ct) ?? 0;

        db.PromptVersions.Add(new PromptVersion
        {
            EntityType    = "ProjectAgent",
            EntityId      = memberId,
            PromptType    = "user_prompt",
            Content       = request.UserPrompt,
            Version       = latestVersion + 1,
            ChangedBy     = user.Email,
            ChangeReason  = request.ChangeReason
        });

        agent.UserPrompt = request.UserPrompt;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
