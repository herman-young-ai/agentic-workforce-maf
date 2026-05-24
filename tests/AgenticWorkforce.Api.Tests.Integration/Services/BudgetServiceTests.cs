using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Events;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Services;

/// <summary>
/// Phase 6 plan §Verification §4 — <c>BudgetService</c> integration: spend
/// derives from the sum of <c>LlmCall.CostUsd</c> rows; warnings publish
/// <see cref="EventTypes.BudgetWarning"/> events; exhaustion publishes
/// <see cref="EventTypes.BudgetExhausted"/>. Runs against the Testcontainers
/// PostgreSQL — no mocks, real partitioned schema.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class BudgetServiceTests(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Project> SeedProjectAsync(AppDbContext db, decimal? ceilingUsd, Guid ownerId)
    {
        await _factory.SeedUserAsync(ownerId, $"owner-{ownerId:N}@test.local");
        var project = new Project
        {
            Id               = Guid.NewGuid(),
            Name             = $"Budget-{Guid.NewGuid():N}",
            Objective        = "Budget integration",
            Status           = ProjectStatus.Active,
            Tier             = ProjectTier.User,
            BudgetCeilingUsd = ceilingUsd
        };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId    = ownerId,
            Role      = ProjectRole.Owner
        });
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task AddLlmCallAsync(AppDbContext db, Guid projectId, decimal cost)
    {
        db.LlmCalls.Add(new LlmCall
        {
            ProjectId = projectId,
            Model     = "stub-model",
            Provider  = "stub",
            CostUsd   = cost
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetStatusAsync_SumsLlmCallCostUsd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sut = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        var project = await SeedProjectAsync(db, ceilingUsd: 10m, ownerId: Guid.NewGuid());
        await AddLlmCallAsync(db, project.Id, 0.25m);
        await AddLlmCallAsync(db, project.Id, 0.75m);

        var status = await sut.GetStatusAsync(project.Id);

        status.CeilingUsd.Should().Be(10m);
        status.UsedUsd.Should().Be(1.0m);
        status.RemainingUsd.Should().Be(9.0m);
        status.IsExhausted.Should().BeFalse();
    }

    [Fact]
    public async Task CanSpendAsync_DeniesWhenNoCeilingConfigured()
    {
        // Principle 14 — missing budget config must deny, never default to unbounded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sut = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        var project = await SeedProjectAsync(db, ceilingUsd: null, ownerId: Guid.NewGuid());

        var allowed = await sut.CanSpendAsync(project.Id, sessionId: null, estimatedCostUsd: 0.01m);

        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task RecordSpendAsync_AtThreshold_PublishesBudgetWarningEvent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sut = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        var project = await SeedProjectAsync(db, ceilingUsd: 1m, ownerId: Guid.NewGuid());
        // Default threshold is 80%. Push used >= 0.80.
        await AddLlmCallAsync(db, project.Id, 0.85m);

        await sut.RecordSpendAsync(project.Id, sessionId: null, taskId: null, costUsd: 0.85m);

        var events = await db.ProjectEvents
            .AsNoTracking()
            .Where(e => e.ProjectId == project.Id)
            .ToListAsync();

        events.Should().Contain(e => e.EventType == EventTypes.BudgetWarning);
        events.Should().NotContain(e => e.EventType == EventTypes.BudgetExhausted);
    }

    [Fact]
    public async Task RecordSpendAsync_WhenExhausted_PublishesBudgetExhaustedEvent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sut = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        var project = await SeedProjectAsync(db, ceilingUsd: 1m, ownerId: Guid.NewGuid());
        await AddLlmCallAsync(db, project.Id, 1.10m);

        await sut.RecordSpendAsync(project.Id, sessionId: null, taskId: null, costUsd: 1.10m);

        var events = await db.ProjectEvents
            .AsNoTracking()
            .Where(e => e.ProjectId == project.Id)
            .ToListAsync();

        events.Should().Contain(e => e.EventType == EventTypes.BudgetExhausted);
    }

    [Fact]
    public async Task RecordSpendAsync_BelowThreshold_PublishesNoEvent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sut = scope.ServiceProvider.GetRequiredService<IBudgetService>();

        var project = await SeedProjectAsync(db, ceilingUsd: 10m, ownerId: Guid.NewGuid());
        await AddLlmCallAsync(db, project.Id, 1.00m); // 10% utilisation

        await sut.RecordSpendAsync(project.Id, sessionId: null, taskId: null, costUsd: 1.00m);

        var events = await db.ProjectEvents
            .AsNoTracking()
            .Where(e => e.ProjectId == project.Id
                     && (e.EventType == EventTypes.BudgetWarning
                      || e.EventType == EventTypes.BudgetExhausted))
            .ToListAsync();

        events.Should().BeEmpty();
    }
}
