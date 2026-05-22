using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Catalog;

public static class AdminCreateAgent
{
    public record Request(
        string AgentName,
        string? AgentType,
        string? AgentVersion,
        string? Description,
        string? SystemPrompt,
        string? ModelConfig,
        AgentVisibility Visibility = AgentVisibility.Public,
        bool ChatEnabled = false);

    public record Response(Guid Id, string AgentName, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/catalog", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminCatalog");

    private static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        IAgentCatalogRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName))
            throw new ValidationException("AgentName is required.");

        if (await repo.GetByNameAsync(request.AgentName, ct) is not null)
            throw new AlreadyExistsException("AgentCatalog", $"name '{request.AgentName}'");

        var agent = await repo.AddAsync(new AgentCatalog
        {
            AgentName    = request.AgentName,
            AgentType    = request.AgentType,
            AgentVersion = request.AgentVersion,
            Description  = request.Description,
            SystemPrompt = request.SystemPrompt,
            ModelConfig  = request.ModelConfig,
            Visibility   = request.Visibility,
            ChatEnabled  = request.ChatEnabled,
            Enabled      = true,
            Keywords     = []
        }, ct);

        return Results.Created(
            $"/api/v1/admin/catalog/{agent.Id}",
            new Response(agent.Id, agent.AgentName, agent.CreatedAt));
    }
}
