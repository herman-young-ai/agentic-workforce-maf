using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Milestones;

public static class CreateMilestone
{
    public record Request(
        string Title,
        string Summary,
        DateTime PeriodStart,
        DateTime PeriodEnd,
        string? WorkflowRunIds = null,
        string? KeyOutcomes = null,
        string[]? DomainTags = null);

    public record Response(Guid Id, DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/milestones", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .WithTags("Milestones");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IMilestoneRepository repo,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Operator, ct);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("Title is required.");
        if (request.PeriodEnd <= request.PeriodStart)
            throw new ValidationException("PeriodEnd must be after PeriodStart.");

        var milestone = await repo.AddAsync(new MilestoneSummary
        {
            ProjectId      = projectId,
            Title          = request.Title,
            Summary        = request.Summary,
            PeriodStart    = request.PeriodStart,
            PeriodEnd      = request.PeriodEnd,
            WorkflowRunIds = request.WorkflowRunIds,
            KeyOutcomes    = request.KeyOutcomes,
            DomainTags     = request.DomainTags ?? []
        }, ct);

        return Results.Created(
            $"/api/v1/projects/{projectId}/milestones/{milestone.Id}",
            new Response(milestone.Id, milestone.CreatedAt));
    }
}
