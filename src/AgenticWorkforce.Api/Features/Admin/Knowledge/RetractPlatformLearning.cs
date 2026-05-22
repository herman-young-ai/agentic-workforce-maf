using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Admin.Knowledge;

public static class RetractPlatformLearning
{
    public record Request(string Reason);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/admin/knowledge/learnings/{learningId:guid}/retract", HandleAsync)
            .RequireAuthorization(Policies.RequirePlatformAdmin)
            .WithTags("AdminKnowledge");

    private static async Task<IResult> HandleAsync(
        Guid learningId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        ILearningRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Reason is required.");

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);

        await repo.RetractAsync(learningId, user.Email, request.Reason, ct);
        return Results.NoContent();
    }
}
