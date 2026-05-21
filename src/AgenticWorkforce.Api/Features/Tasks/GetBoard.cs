using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Tasks;

public static class GetBoard
{
    public record TaskCard(
        Guid Id,
        TaskType Type,
        string Objective,
        string? AgentName,
        TaskSource Source,
        decimal CostUsd,
        Guid? ParentTaskId,
        IReadOnlyList<Guid> DependsOn,
        DateTime CreatedAt);

    public record Response(IReadOnlyDictionary<string, IReadOnlyList<TaskCard>> Columns);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/projects/{projectId:guid}/tasks/board", HandleAsync)
            .RequireAuthorization(Policies.RequireViewer)
            .WithTags("Tasks")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        ITaskRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Viewer, ct);

        var tasks = await repo.GetBoardAsync(projectId, ct);

        var columns = tasks
            .GroupBy(t => t.Status.ToString())
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskCard>)g.Select(t => new TaskCard(
                    t.Id, t.Type, t.Objective, t.AgentName, t.Source, t.CostUsd,
                    t.ParentTaskId,
                    t.Dependencies.Select(d => d.DependsOnTaskId).ToList(),
                    t.CreatedAt)).ToList());

        return Results.Ok(new Response(columns));
    }
}
