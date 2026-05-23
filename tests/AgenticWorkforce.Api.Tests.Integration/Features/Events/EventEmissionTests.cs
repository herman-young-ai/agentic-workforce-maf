using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Api.Features.Tasks;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Disambiguate AgenticWorkforce.Domain.Enums.TaskStatus from
// System.Threading.Tasks.TaskStatus (both are in scope via implicit usings).
using TaskStatus = AgenticWorkforce.Domain.Enums.TaskStatus;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Events;

/// <summary>
/// Phase-4 mutating endpoints used to silently produce zero audit rows —
/// the Phase-5 event pipeline existed but no production code published to
/// it. These tests prove the wiring works end-to-end: mutating an entity
/// via the API durably records a matching <c>project_events</c> row in
/// the same transaction as the business change.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class EventEmissionTests(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateProject_EmitsProjectCreatedEvent()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, "creator@events.local");
        var client = _factory.CreateAuthenticatedClient(ownerId, "creator@events.local", Roles.Owner);

        var resp = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"events-{Guid.NewGuid():N}",
            objective = "Event emission test"
        });
        resp.EnsureSuccessStatusCode();
        var project = (await resp.Content.ReadFromJsonAsync<CreateProject.Response>())!;

        // The event row must exist by the time the HTTP response returns —
        // both the project and the event commit in the same SaveChanges.
        var evt = await ReadProjectEventAsync(project.Id, EventTypes.ProjectCreated);
        evt.Source.Should().Be("creator@events.local");
        evt.Severity.Should().Be(EventSeverity.Info);
    }

    [Fact]
    public async Task ApproveTask_EmitsTaskApprovedEvent_TransactionalWithStatusChange()
    {
        // Owner creates a project and a Proposed task.
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, "owner@events.local");
        var ownerClient = _factory.CreateAuthenticatedClient(ownerId, "owner@events.local", Roles.Owner);

        var projResp = await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"approve-{Guid.NewGuid():N}",
            objective = "Approve-event test"
        });
        projResp.EnsureSuccessStatusCode();
        var projectId = (await projResp.Content.ReadFromJsonAsync<CreateProject.Response>())!.Id;

        var taskResp = await ownerClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
        {
            objective = "approve-me"
        });
        taskResp.EnsureSuccessStatusCode();
        var taskId = (await taskResp.Content.ReadFromJsonAsync<CreateTask.Response>())!.Id;

        // A different reviewer approves (SoD: creator ≠ approver).
        var reviewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(reviewerId, "reviewer@events.local");
        await AddReviewerAsync(ownerClient, projectId, reviewerId, "reviewer@events.local");
        var reviewerClient = _factory.CreateAuthenticatedClient(reviewerId, "reviewer@events.local", Roles.Reviewer);

        var approveResp = await reviewerClient.PostAsync(
            $"/api/v1/projects/{projectId}/tasks/{taskId}/approve", content: null);
        approveResp.EnsureSuccessStatusCode();

        // Both the status change AND the event must be persisted — the
        // interceptor's transactional-outbox guarantee.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.FirstAsync(t => t.Id == taskId);
        task.Status.Should().Be(TaskStatus.Approved);

        var evt = await ReadProjectEventAsync(projectId, EventTypes.TaskApproved);
        evt.TaskId.Should().Be(taskId);
        evt.Source.Should().Be("reviewer@events.local");
    }

    private async Task<AgenticWorkforce.Domain.Entities.ProjectEvent> ReadProjectEventAsync(
        Guid projectId, string eventType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.ProjectEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProjectId == projectId && e.EventType == eventType);
        evt.Should().NotBeNull(
            $"a {eventType} row should have been written for project {projectId}");
        return evt!;
    }

    private static async Task AddReviewerAsync(
        HttpClient ownerClient, Guid projectId, Guid reviewerId, string _)
    {
        var addResp = await ownerClient.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/members",
            new { UserId = reviewerId, Role = ProjectRole.Reviewer });
        addResp.EnsureSuccessStatusCode();
    }
}
