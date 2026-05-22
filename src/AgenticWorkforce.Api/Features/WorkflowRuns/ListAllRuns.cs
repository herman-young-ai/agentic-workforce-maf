using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Pagination;

namespace AgenticWorkforce.Api.Features.WorkflowRuns;

public static class ListAllRuns
{
    public record Response(
        Guid Id,
        Guid WorkflowDefinitionId,
        string WorkflowName,
        int WorkflowVersion,
        WorkflowRunStatus Status,
        decimal TotalCostUsd,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/workflow-runs", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("WorkflowRuns");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [AsParameters] PagedQuery paging,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IWorkflowRunRepository runs,
        CancellationToken ct)
    {
        await authz.EnsureRoleAsync(userAccessor.User.Id, projectId, ProjectRole.Viewer, ct);

        var page = await runs.ListByProjectPagedAsync(projectId, workflowDefinitionId: null, paging, ct);
        var items = page.Items
            .Select(r => new Response(r.Id, r.WorkflowDefinitionId, r.WorkflowName, r.WorkflowVersion,
                r.Status, r.TotalCostUsd, r.CreatedAt))
            .ToList();
        return Results.Ok(new PagedResult<Response>(items, page.Page, page.PageSize, page.TotalCount));
    }
}
