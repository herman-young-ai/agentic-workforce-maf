using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminUpdatePrompt
{
    private const string EntityTypeAgentCatalog = "AgentCatalog";
    private const string PromptTypeSystemPrompt = "system_prompt";

    public record Request(string SystemPrompt, string? ChangeReason = null);
    public record Response(int Version);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPut("/api/v1/admin/catalog/{agentId:guid}/prompt", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IAgentCatalogRepository catalog,
        IPromptVersionRepository promptVersions,
        CancellationToken ct)
    {
        var user = userAccessor.User;

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
            throw new ValidationException("SystemPrompt cannot be empty.");

        var agent = await catalog.GetByIdAsync(agentId, ct)
            ?? throw new NotFoundException("AgentCatalog", agentId);

        var current = await promptVersions.GetCurrentVersionAsync(EntityTypeAgentCatalog, agentId, ct);

        await promptVersions.AddAsync(new PromptVersion
        {
            EntityType   = EntityTypeAgentCatalog,
            EntityId     = agentId,
            PromptType   = PromptTypeSystemPrompt,
            Content      = request.SystemPrompt,
            Version      = current + 1,
            ChangedBy    = user.Email,
            ChangeReason = request.ChangeReason
        }, ct);

        agent.SystemPrompt = request.SystemPrompt;
        await catalog.UpdateAsync(agent, ct);

        return Results.Ok(new Response(current + 1));
    }
}
