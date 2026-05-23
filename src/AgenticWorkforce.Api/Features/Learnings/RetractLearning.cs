using System.Text.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class RetractLearning
{
    public record Request(string Reason);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}/retract", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        IEventPublisher publisher,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Reason is required.");

        var l = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (l.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);

        await publisher.PublishAsync(new ProjectEvent
        {
            ProjectId = projectId,
            EventType = EventTypes.LearningRetracted,
            Source    = user.Email,
            Severity  = EventSeverity.Warning,
            Data      = JsonSerializer.Serialize(new { LearningId = learningId, l.Title, request.Reason })
        }, ct);

        await repo.RetractAsync(learningId, user.Email, request.Reason, ct);
        return Results.NoContent();
    }
}
