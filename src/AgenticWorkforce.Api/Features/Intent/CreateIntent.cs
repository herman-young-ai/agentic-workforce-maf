using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Intent;

public static class CreateIntent
{
    public record Request(
        string IntentText,
        string IntentSummary,
        string Scope,
        string Reason);

    public record Response(Guid Id, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/intent", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Intent");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIntentRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.IntentText))
            throw new ValidationException("Intent text is required.");

        var existing = await repo.GetCurrentAsync(projectId, ct);

        var intent = await repo.AddAsync(new ProjectIntent
        {
            ProjectId     = projectId,
            Intent        = request.IntentText,
            IntentSummary = request.IntentSummary,
            Scope         = request.Scope,
            Source        = IntentSource.UserChat,
            Reason        = request.Reason,
            RevisedFromId = existing?.Id
        }, ct);

        return Results.Created(
            $"/api/v1/projects/{projectId}/intent",
            new Response(intent.Id, intent.CreatedAt));
    }
}
