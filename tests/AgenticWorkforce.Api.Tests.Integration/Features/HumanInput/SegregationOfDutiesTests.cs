using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.HumanInput;

/// <summary>
/// Principle 11 — Segregation of Duties. The user who triggers a workflow
/// run cannot respond to its human input requests. Enforced inside
/// IHumanInputRepository.RespondAsync via the typed
/// WorkflowRun.TriggeredById FK (added in Phase 3.5).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SegregationOfDutiesTests(ApiWebApplicationFactory factory)
    : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Respond_TriggeringUserIsResponder_Returns403()
    {
        var (triggerId, projectId, requestId) = await SetupHumanInputRequest();

        await _factory.SeedUserAsync(triggerId, $"{triggerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(
            triggerId, $"{triggerId}@test.local", Roles.Reviewer);

        // Add the triggering user as a Reviewer of the project so the BOLA
        // check passes and SOD is the only thing left to enforce.
        await AddProjectMember(projectId, triggerId, ProjectRole.Reviewer);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/human-input/{requestId}/respond",
            new { Decision = HumanDecisionType.Approved, Response = "ok" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Respond_DifferentReviewer_Returns204()
    {
        var (_, projectId, requestId) = await SetupHumanInputRequest();

        var otherReviewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(otherReviewerId, $"{otherReviewerId}@test.local");
        await AddProjectMember(projectId, otherReviewerId, ProjectRole.Reviewer);
        using var client = _factory.CreateAuthenticatedClient(
            otherReviewerId, $"{otherReviewerId}@test.local", Roles.Reviewer);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/human-input/{requestId}/respond",
            new { Decision = HumanDecisionType.Approved, Response = "ok" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Seeds a project, workflow definition, workflow run (triggered by a
    /// known user), task, and a pending human input request. Returns the
    /// triggering user id, project id, and request id.
    /// </summary>
    private async Task<(Guid TriggerId, Guid ProjectId, Guid RequestId)> SetupHumanInputRequest()
    {
        var triggerId = Guid.NewGuid();
        await _factory.SeedUserAsync(triggerId, $"{triggerId}@test.local");
        using var ownerClient = _factory.CreateAuthenticatedClient(
            triggerId, $"{triggerId}@test.local", Roles.Owner);

        var project = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"SOD-{Guid.NewGuid():N}",
            Objective = "SOD test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var workflow = new WorkflowDefinition
        {
            ProjectId   = project!.Id,
            Name        = "sod-test",
            Version     = 1,
            Enabled     = true,
            Nodes       = "[]",
            Edges       = "[]"
        };
        db.WorkflowDefinitions.Add(workflow);

        var run = new WorkflowRun
        {
            ProjectId            = project.Id,
            WorkflowDefinitionId = workflow.Id,
            WorkflowDefinition   = workflow,
            WorkflowName         = workflow.Name,
            WorkflowVersion      = workflow.Version,
            Status               = WorkflowRunStatus.AwaitingInput,
            TriggeredBy          = $"{triggerId}@test.local",
            TriggeredById        = triggerId
        };
        db.WorkflowRuns.Add(run);

        var task = new AgenticTask
        {
            ProjectId    = project.Id,
            Type         = TaskType.HumanDecision,
            Status       = TaskStatus.Running,
            Objective    = "Approve change",
            Source       = TaskSource.Workflow,
            WorkflowRunId = run.Id,
            WorkflowRun   = run
        };
        db.Tasks.Add(task);

        var request = new HumanInputRequest
        {
            ProjectId       = project.Id,
            WorkflowRunId   = run.Id,
            WorkflowRun     = run,
            TaskId          = task.Id,
            Task            = task,
            PromptMessage   = "Approve?",
            Status          = HumanInputRequestStatus.Pending
        };
        db.HumanInputRequests.Add(request);

        await db.SaveChangesAsync();
        return (triggerId, project.Id, request.Id);
    }

    private async Task AddProjectMember(Guid projectId, Guid userId, ProjectRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId);
        if (existing is not null)
        {
            existing.Role = role;
        }
        else
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId    = userId,
                Role      = role
            });
        }
        await db.SaveChangesAsync();
    }
}
