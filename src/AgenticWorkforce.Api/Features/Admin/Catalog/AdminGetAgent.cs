using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminGetAgent
{
    public record Response(
        Guid Id,
        string AgentName,
        string? AgentType,
        string? AgentVersion,
        string? Description,
        string? SystemPrompt,
        string? ModelConfig,
        string? Tools,
        string? Scope,
        string? Constraints,
        string[] Keywords,
        string? ThinkingBudget,
        bool Enabled,
        bool ChatEnabled,
        AgentVisibility Visibility,
        decimal? MaxBudgetUsd,
        int? MaxInputLength,
        bool ProducesArtifact,
        string? ArtifactType);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/admin/catalog/{agentId:guid}", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        var a = await repo.GetByIdAsync(agentId, ct)
            ?? throw new NotFoundException("AgentCatalog", agentId);

        return Results.Ok(new Response(
            a.Id, a.AgentName, a.AgentType, a.AgentVersion, a.Description, a.SystemPrompt,
            a.ModelConfig, a.Tools, a.Scope, a.Constraints, a.Keywords, a.ThinkingBudget,
            a.Enabled, a.ChatEnabled, a.Visibility, a.MaxBudgetUsd, a.MaxInputLength,
            a.ProducesArtifact, a.ArtifactType));
    }
}
