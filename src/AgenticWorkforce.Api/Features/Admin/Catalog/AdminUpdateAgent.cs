using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminUpdateAgent
{
    public record Request(
        string? Description,
        string? SystemPrompt,
        string? ModelConfig,
        string? Constraints,
        string[]? Keywords,
        AgentVisibility? Visibility,
        bool? ChatEnabled,
        decimal? MaxBudgetUsd,
        int? MaxInputLength);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/v1/admin/catalog/{agentId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        [FromBody] Request request,
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        var agent = await repo.GetByIdAsync(agentId, ct)
            ?? throw new NotFoundException("AgentCatalog", agentId);

        if (request.Description is not null) agent.Description = request.Description;
        if (request.SystemPrompt is not null) agent.SystemPrompt = request.SystemPrompt;
        if (request.ModelConfig is not null) agent.ModelConfig = request.ModelConfig;
        if (request.Constraints is not null) agent.Constraints = request.Constraints;
        if (request.Keywords is not null) agent.Keywords = request.Keywords;
        if (request.Visibility.HasValue) agent.Visibility = request.Visibility.Value;
        if (request.ChatEnabled.HasValue) agent.ChatEnabled = request.ChatEnabled.Value;
        if (request.MaxBudgetUsd.HasValue) agent.MaxBudgetUsd = request.MaxBudgetUsd.Value;
        if (request.MaxInputLength.HasValue) agent.MaxInputLength = request.MaxInputLength.Value;

        await repo.UpdateAsync(agent, ct);
        return Results.NoContent();
    }
}
