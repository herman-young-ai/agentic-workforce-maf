using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Learnings;

public static class SupersedeLearning
{
    public record Request(
        LearningKind Kind,
        string Title,
        string Body,
        string? Recommendation,
        decimal Confidence,
        string[]? AgentNames = null,
        string[]? DomainTags = null);

    public record Response(Guid NewLearningId, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/learnings/{learningId:guid}/supersede", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Learnings");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid learningId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ILearningRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("Title is required.");
        if (request.Confidence < 0 || request.Confidence > 1)
            throw new ValidationException("Confidence must be between 0 and 1.");

        var old = await repo.GetByIdAsync(learningId, ct)
            ?? throw new NotFoundException("Learning", learningId);
        if (old.ProjectId != projectId)
            throw new NotFoundException("Learning", learningId);

        var replacement = new ProjectLearning
        {
            ProjectId       = projectId,
            Kind            = request.Kind,
            Title           = request.Title,
            Body            = request.Body,
            Recommendation  = request.Recommendation,
            Confidence      = request.Confidence,
            AgentNames      = request.AgentNames ?? [],
            DomainTags      = request.DomainTags ?? [],
            Status          = LearningStatus.Active,
            FormatVersion   = "1.0"
        };

        await repo.SupersedeAsync(learningId, replacement, ct);
        return Results.Ok(new Response(replacement.Id, replacement.CreatedAt));
    }
}
