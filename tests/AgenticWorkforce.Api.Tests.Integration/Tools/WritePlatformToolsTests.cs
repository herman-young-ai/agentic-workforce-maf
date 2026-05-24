using System.Text.Json;
using AgenticWorkforce.Agents.Tools.Project;
using AgenticWorkforce.Agents.Tools.Supervisor;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Tools;

/// <summary>
/// Phase 7d verification: the supervisor + write Platform tools land their
/// real effects on the database under the platform service-account identity,
/// and tasks created by the write tools default to <see cref="TaskStatus.Proposed"/>
/// (Principle 17 — human approval before execution).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class WritePlatformToolsTests(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Project> SeedProjectAsync()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"owner-{ownerId:N}@test.local");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project
        {
            Id        = Guid.NewGuid(),
            Name      = $"WriteTools-{Guid.NewGuid():N}",
            Objective = "Phase 7d write tools",
            Status    = ProjectStatus.Active,
            Tier      = ProjectTier.User,
            BudgetCeilingUsd = 1m
        };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId    = ownerId,
            Role      = ProjectRole.Owner
        });
        // Seed a minimal PCD row so AddPrincipleTool has somewhere to append.
        // Production hand-rolls this in CreateProject's repository path; the test
        // shortcuts straight to the entity since it does not exercise that endpoint.
        db.ProjectContexts.Add(new ProjectContext
        {
            ProjectId      = project.Id,
            ContextData    = "{}",
            ContextVersion = 1,
            SizeCharacters = 2,
            SizeTokens     = 1,
            FormatVersion  = "1.0"
        });
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public void PlatformActor_ResolvesFromConfig()
    {
        using var scope = _factory.Services.CreateScope();
        var actor = scope.ServiceProvider.GetRequiredService<IPlatformActor>();

        actor.UserId.Should().Be(Guid.Parse("00000000-0000-0000-0000-00000000a9e7"));
        actor.Email.Should().Be("platform-agent@awp.test");
    }

    [Fact]
    public async Task PlatformActorSeeder_CreatesServiceAccountRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = scope.ServiceProvider.GetRequiredService<IPlatformActor>();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actor.UserId);

        user.Should().NotBeNull();
        user!.IsServiceAccount.Should().BeTrue();
        user.Email.Should().Be(actor.Email);
    }

    [Fact]
    public async Task RunObjective_CreatesProposedTaskAttributedToPlatformActor()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var tool = (AIFunction)new RunObjectiveTool.Factory().Create(scope.ServiceProvider, project.Id);

        var raw = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["objective"] = "Audit the auth module for OWASP A07.",
            ["agentName"] = "security.webapp.scanner"
        });

        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Proposed");
        var taskId = doc.RootElement.GetProperty("taskId").GetGuid();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        task.ProjectId.Should().Be(project.Id);
        task.AgentName.Should().Be("security.webapp.scanner");
        task.Status.Should().Be(TaskStatus.Proposed);
        task.Source.Should().Be(TaskSource.AdHoc);
        task.CreatedById.Should().Be(scope.ServiceProvider.GetRequiredService<IPlatformActor>().UserId);
    }

    [Fact]
    public async Task StartResearch_CreatesProposedTaskAssignedToStrategist()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var tool = (AIFunction)new StartResearchTool.Factory().Create(scope.ServiceProvider, project.Id);

        var raw = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["question"] = "What is the regulatory exposure for storing customer chat logs in eu-west-1?",
            ["depth"]    = "deep"
        });

        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Proposed");
        doc.RootElement.GetProperty("agentName").GetString().Should().Be("research.strategist");

        var taskId = doc.RootElement.GetProperty("taskId").GetGuid();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        task.Inputs.Should().NotBeNullOrWhiteSpace();
        task.Inputs.Should().Contain("regulatory exposure");
        task.Inputs.Should().Contain("deep");
    }

    [Fact]
    public async Task AddPrinciple_AppendsToPcdUnderPlatformActor()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var tool = (AIFunction)new AddPrincipleTool.Factory().Create(scope.ServiceProvider, project.Id);

        var principle = "Every database write must be reversible via a documented retraction path.";
        var raw = await tool.InvokeAsync(new AIFunctionArguments { ["principle"] = principle });

        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.GetProperty("addedBy").GetString().Should().Be("platform-agent@awp.test");

        var pcd = await scope.ServiceProvider
            .GetRequiredService<IProjectContextService>()
            .GetAsync(project.Id);
        pcd.ContextData.Should().Contain(principle);
    }

    [Fact]
    public async Task GetRecentOutcomes_EmptyProject_ReturnsEmptyArray()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var tool = (AIFunction)new GetRecentOutcomesTool.Factory().Create(scope.ServiceProvider, project.Id);

        var raw = await tool.InvokeAsync(new AIFunctionArguments());
        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetRecentOutcomes_SurfacesCompletedTasksPastInFlightOnes()
    {
        // Regression guard for the original "filter in memory after paging" bug:
        // many in-flight tasks would push the most recent completed one off the page.
        var project = await SeedProjectAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 30 proposed tasks (in-flight) created BEFORE the completed task — these would
            // dominate any creation-time ordering.
            for (var i = 0; i < 30; i++)
            {
                db.Tasks.Add(new AgenticTask
                {
                    ProjectId = project.Id,
                    Objective = $"in-flight {i}",
                    AgentName = "test.agent",
                    Type      = TaskType.AgentTask,
                    Status    = TaskStatus.Proposed,
                    Source    = TaskSource.Manual
                });
            }
            // One completed task created after the proposals — this is what the supervisor
            // wants to see when asking for "recent outcomes".
            db.Tasks.Add(new AgenticTask
            {
                ProjectId   = project.Id,
                Objective   = "completed work",
                AgentName   = "test.agent",
                Type        = TaskType.AgentTask,
                Status      = TaskStatus.Completed,
                Source      = TaskSource.Manual,
                StartedAt   = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                CostUsd     = 0.01m
            });
            await db.SaveChangesAsync();
        }

        using var queryScope = _factory.Services.CreateScope();
        var tool = (AIFunction)new GetRecentOutcomesTool.Factory().Create(queryScope.ServiceProvider, project.Id);

        var raw = await tool.InvokeAsync(new AIFunctionArguments { ["count"] = 5 });
        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.GetArrayLength().Should().Be(1, "the one Completed task must surface even with 30 in-flight tasks ahead of it on creation time.");
        doc.RootElement[0].GetProperty("status").GetString().Should().Be("Completed");
        doc.RootElement[0].GetProperty("objective").GetString().Should().Be("completed work");
    }

    [Fact]
    public async Task GetPastDecisions_EmptyProject_ReturnsEmptyArray()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var tool = (AIFunction)new GetPastDecisionsTool.Factory().Create(scope.ServiceProvider, project.Id);

        var raw = await tool.InvokeAsync(new AIFunctionArguments());
        using var doc = JsonDocument.Parse(raw!.ToString()!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }
}
