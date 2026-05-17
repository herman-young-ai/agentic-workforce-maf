using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration;

public class DatabaseSmokeTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public Task InitializeAsync() => _factory.StartAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Database_MigratesAndConnects_Successfully()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        (await db.Database.CanConnectAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ProjectRepository_CreateAndRetrieve_Works()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var repo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var project = new Project
        {
            Name = $"Smoke {Guid.NewGuid():N}",
            Objective = "Verify data layer wiring"
        };

        await repo.CreateAsync(project);

        var retrieved = await repo.GetByIdAsync(project.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be(project.Name);
        retrieved.Objective.Should().Be("Verify data layer wiring");
    }

    [Fact]
    public async Task PartitionedTables_AcceptInsertsAfterMigration()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // project_events and llm_calls are partitioned; an insert here proves the
        // partitions exist for the current month and the schema matches the entity.
        var project = new Project { Name = $"Partition {Guid.NewGuid():N}", Objective = "Partition smoke" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectEvents.Add(new ProjectEvent
        {
            ProjectId = project.Id,
            EventType = "smoke.test",
            Source = "DatabaseSmokeTests"
        });
        db.LlmCalls.Add(new LlmCall
        {
            ProjectId = project.Id,
            Model = "test-model",
            Provider = "test"
        });

        await db.SaveChangesAsync();

        (await db.ProjectEvents.CountAsync(e => e.ProjectId == project.Id)).Should().Be(1);
        (await db.LlmCalls.CountAsync(l => l.ProjectId == project.Id)).Should().Be(1);
    }
}
