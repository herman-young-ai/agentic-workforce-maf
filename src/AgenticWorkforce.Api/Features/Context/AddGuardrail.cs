using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Context;

public static class AddGuardrail
{
    public record Request(string Text);
    public record Response(string GuardrailId);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/context/guardrails", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Context");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IProjectContextService svc,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        var id = await svc.AddGuardrailAsync(projectId, request.Text, user.Id, ct);
        return Results.Created(
            $"/api/v1/projects/{projectId}/context/guardrails/{id}",
            new Response(id));
    }
}
