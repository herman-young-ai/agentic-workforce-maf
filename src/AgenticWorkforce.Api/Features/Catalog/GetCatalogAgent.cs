using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Catalog;

public static class GetCatalogAgent
{
    public record Response(
        Guid Id,
        string AgentName,
        string? AgentType,
        string? AgentVersion,
        string? Description,
        string? Engine,
        string[] Keywords,
        AgentVisibility Visibility,
        bool ChatEnabled,
        bool ProducesArtifact,
        string? ArtifactType);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/catalog/{agentId:guid}", HandleAsync)
            .RequireAuthorization()
            .WithTags("Catalog");

    private static async Task<IResult> HandleAsync(
        Guid agentId,
        ICurrentUserAccessor userAccessor,
        ICatalogQueryRepository repo,
        CancellationToken ct)
    {
        var isAdmin = userAccessor.User.IsInRole(Roles.PlatformAdmin);
        var a = await repo.GetByIdVisibleAsync(agentId, isAdmin, ct)
            ?? throw new NotFoundException("CatalogAgent", agentId);

        return Results.Ok(new Response(
            a.Id, a.AgentName, a.AgentType, a.AgentVersion, a.Description, a.Engine,
            a.Keywords, a.Visibility, a.ChatEnabled, a.ProducesArtifact, a.ArtifactType));
    }
}
