using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Api.Features.Tasks;
using AgenticWorkforce.Domain.Enums;
using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Tasks;

[Collection(IntegrationTestCollection.Name)]
public class TaskLifecycleTests(ApiWebApplicationFactory factory)
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

    // Convenience: create a project as Owner and return the project ID
    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Task-Proj-{Guid.NewGuid():N}",
            Objective = "Task lifecycle tests"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateProject.Response>();
        return body!.Id;
    }

    // Convenience: create a task in Proposed state and return the task ID
    private static async Task<Guid> CreateTaskAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/tasks",
            new { Objective = $"Task-{Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateTask.Response>();
        return body!.Id;
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/projects/{projectId}/tasks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateTask_AsOwner_Returns201WithProposedStatus()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var projectId = await CreateProjectAsync(client);
        var response  = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
        {
            Objective = "Run report"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateTask.Response>();
        body!.Status.Should().Be(TaskStatus.Proposed);
        body.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task CreateTask_AsViewer_Returns403()
    {
        var ownerId   = Guid.NewGuid();
        var viewerId  = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId,  $"{ownerId}@test.local");
        await _factory.SeedUserAsync(viewerId, $"{viewerId}@test.local");

        using var ownerClient  = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);
        var projectId = await CreateProjectAsync(ownerClient);

        // Add viewer to project members via owner
        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new
        {
            UserId = viewerId,
            Role   = ProjectRole.Viewer
        });

        using var viewerClient = _factory.CreateAuthenticatedClient(viewerId, $"{viewerId}@test.local", Roles.Viewer);
        var response = await viewerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
        {
            Objective = "Should be denied"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/projects/{projectId}/tasks/{taskId}/approve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ApproveTask_AsCreator_Returns403_SegregationOfDuties()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        // Owner has Reviewer + Owner roles — but SOD must still be enforced
        using var client = _factory.CreateAuthenticatedClient(
            ownerId, $"{ownerId}@test.local", Roles.Owner, Roles.Reviewer);

        var projectId = await CreateProjectAsync(client);
        var taskId    = await CreateTaskAsync(client, projectId);

        var response = await client.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{taskId}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApproveTask_AsDifferentReviewer_Returns204()
    {
        var ownerId    = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId,    $"{ownerId}@test.local");
        await _factory.SeedUserAsync(reviewerId, $"{reviewerId}@test.local");

        using var ownerClient = _factory.CreateAuthenticatedClient(
            ownerId, $"{ownerId}@test.local", Roles.Owner);
        var projectId = await CreateProjectAsync(ownerClient);
        var taskId    = await CreateTaskAsync(ownerClient, projectId);

        // Add reviewer to project members
        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new
        {
            UserId = reviewerId,
            Role   = ProjectRole.Reviewer
        });

        using var reviewerClient = _factory.CreateAuthenticatedClient(
            reviewerId, $"{reviewerId}@test.local", Roles.Reviewer);
        var response = await reviewerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{taskId}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify task status in DB
        using var scope = _factory.Services.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        task.Status.Should().Be(TaskStatus.Approved);
    }

    [Fact]
    public async Task ApproveTask_AlreadyApproved_Returns400()
    {
        var ownerId    = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId,    $"{ownerId}@test.local");
        await _factory.SeedUserAsync(reviewerId, $"{reviewerId}@test.local");

        using var ownerClient = _factory.CreateAuthenticatedClient(
            ownerId, $"{ownerId}@test.local", Roles.Owner);
        var projectId = await CreateProjectAsync(ownerClient);
        var taskId    = await CreateTaskAsync(ownerClient, projectId);

        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new
        {
            UserId = reviewerId,
            Role   = ProjectRole.Reviewer
        });

        using var reviewerClient = _factory.CreateAuthenticatedClient(
            reviewerId, $"{reviewerId}@test.local", Roles.Reviewer);

        await reviewerClient.PostAsync($"/api/v1/projects/{projectId}/tasks/{taskId}/approve", null);
        var second = await reviewerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{taskId}/approve", null);

        // InvalidStateException → 400
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/projects/{projectId}/tasks/{taskId}/retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RetryTask_NotFailed_Returns400()
    {
        var ownerId    = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId,    $"{ownerId}@test.local");
        await _factory.SeedUserAsync(reviewerId, $"{reviewerId}@test.local");

        using var ownerClient = _factory.CreateAuthenticatedClient(
            ownerId, $"{ownerId}@test.local", Roles.Owner);
        var projectId = await CreateProjectAsync(ownerClient);
        var taskId    = await CreateTaskAsync(ownerClient, projectId);

        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new
        {
            UserId = reviewerId,
            Role   = ProjectRole.Reviewer
        });

        // Approve the task so it's in Approved state (not Failed)
        using var reviewerClient = _factory.CreateAuthenticatedClient(
            reviewerId, $"{reviewerId}@test.local", Roles.Reviewer);
        await reviewerClient.PostAsync($"/api/v1/projects/{projectId}/tasks/{taskId}/approve", null);

        // Retry an Approved task — only Failed tasks can be retried
        var retryResponse = await ownerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{taskId}/retry", null);

        retryResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
