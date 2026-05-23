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

namespace AgenticWorkforce.Api.Tests.Integration.Features.Admin;

/// <summary>
/// Full promotion state machine exercised over the wire:
/// None -> PendingApproval (operator) -> Approved (admin)
/// and the reject path None -> PendingApproval -> Rejected.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminKnowledgePromotionTests(ApiWebApplicationFactory factory)
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
    public async Task FullPromotionFlow_OperatorThenAdmin_ReachesApproved()
    {
        var (projectId, operatorId, learningId) = await SetupLearning();
        var operatorClient = _factory.CreateAuthenticatedClient(
            operatorId, $"{operatorId}@test.local", Roles.Operator);

        var promoteResponse = await operatorClient.PostAsync(
            $"/api/v1/projects/{projectId}/learnings/{learningId}/promote", null);
        promoteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertPromotionStatus(learningId, PromotionStatus.PendingApproval);

        var adminClient = await AdminClient();
        var approveResponse = await adminClient.PostAsync(
            $"/api/v1/admin/knowledge/learnings/{learningId}/approve-promotion", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertPromotionStatus(learningId, PromotionStatus.Approved);
    }

    [Fact]
    public async Task RejectFlow_AdminRejects_TransitionsToRejected()
    {
        var (projectId, operatorId, learningId) = await SetupLearning();
        var operatorClient = _factory.CreateAuthenticatedClient(
            operatorId, $"{operatorId}@test.local", Roles.Operator);

        await operatorClient.PostAsync(
            $"/api/v1/projects/{projectId}/learnings/{learningId}/promote", null);

        var adminClient = await AdminClient();
        var reject = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/knowledge/learnings/{learningId}/reject-promotion",
            new { Reason = "Not generally applicable." });
        reject.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertPromotionStatus(learningId, PromotionStatus.Rejected);
    }

    [Fact]
    public async Task ApprovePromotion_NotPending_Returns400()
    {
        var (_, _, learningId) = await SetupLearning();
        var adminClient = await AdminClient();

        // Skip the promote step entirely → state stays None → admin attempts to approve
        var response = await adminClient.PostAsync(
            $"/api/v1/admin/knowledge/learnings/{learningId}/approve-promotion", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(Guid ProjectId, Guid OperatorId, Guid LearningId)> SetupLearning()
    {
        var operatorId = Guid.NewGuid();
        await _factory.SeedUserAsync(operatorId, $"{operatorId}@test.local");
        var ownerClient = _factory.CreateAuthenticatedClient(
            operatorId, $"{operatorId}@test.local", Roles.Owner);

        var project = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Prom-{Guid.NewGuid():N}",
            Objective = "Promotion flow"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var learning = new ProjectLearning
        {
            ProjectId   = project!.Id,
            Kind        = LearningKind.DomainInsight,
            Title       = "Test learning",
            Body        = "body",
            Confidence  = 0.9m,
            Status      = LearningStatus.Active,
            AgentNames  = [],
            DomainTags  = []
        };
        db.ProjectLearnings.Add(learning);

        // Promote requires the operator to be a project member (they are; owner
        // by virtue of CreateWithOwnerAsync), but raises Role to Operator in
        // the request principal so the policy gate matches.
        await db.SaveChangesAsync();
        return (project.Id, operatorId, learning.Id);
    }

    private async Task<HttpClient> AdminClient()
    {
        var adminId = Guid.NewGuid();
        await _factory.SeedUserAsync(adminId, $"{adminId}@test.local", SystemRole.PlatformAdmin);
        return _factory.CreateAuthenticatedClient(
            adminId, $"{adminId}@test.local", Roles.PlatformAdmin);
    }

    private async Task AssertPromotionStatus(Guid learningId, PromotionStatus expected)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var learning = await db.ProjectLearnings.AsNoTracking().FirstAsync(l => l.Id == learningId);
        learning.PromotionStatus.Should().Be(expected);
    }
}
