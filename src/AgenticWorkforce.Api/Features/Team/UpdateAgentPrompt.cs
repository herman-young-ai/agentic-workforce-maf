using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Team;

public static class UpdateAgentPrompt
{
    // Discriminators for the polymorphic PromptVersions table. Must match what
    // any reader (audit, rollback) uses — keep WRITE and READ paths in lockstep.
    private const string EntityTypeProjectAgent = "ProjectAgent";
    private const string PromptTypeUserPrompt   = "user_prompt";

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
        IProjectAgentRepository projectAgents,
        IPromptVersionRepository promptVersions,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            throw new ValidationException("User prompt cannot be empty.");

        var agent = await projectAgents.GetByIdAsync(memberId, ct);
        if (agent is null || agent.ProjectId != projectId)
            throw new NotFoundException("ProjectAgent", memberId);

        var latestVersion = await promptVersions.GetCurrentVersionAsync(EntityTypeProjectAgent, memberId, ct);

        await promptVersions.AddAsync(new PromptVersion
        {
            EntityType   = EntityTypeProjectAgent,
            EntityId     = memberId,
            PromptType   = PromptTypeUserPrompt,
            Content      = request.UserPrompt,
            Version      = latestVersion + 1,
            ChangedBy    = user.Email,
            ChangeReason = request.ChangeReason
        }, ct);

        agent.UserPrompt = request.UserPrompt;
        await projectAgents.UpdateAsync(agent, ct);

        return Results.NoContent();
    }
}
