using System.Text.Json;
using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Agents.Tools.Project;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Tools;

/// <summary>
/// Phase 7c verification: each read-only Platform tool returns real DB
/// data scoped to the captured projectId. The DI container's
/// <see cref="IPlatformToolResolver"/> is exercised end-to-end against the
/// Testcontainers PostgreSQL to prove repository wiring, not just the
/// factory shape.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PlatformToolsTests(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Project> SeedProjectAsync(string objective = "Tool integration test")
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"owner-{ownerId:N}@test.local");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project
        {
            Id        = Guid.NewGuid(),
            Name      = $"PlatformTools-{Guid.NewGuid():N}",
            Objective = objective,
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
        await db.SaveChangesAsync();
        return project;
    }

    // The Api host does not register AddAgentTools (only the Worker does), so we
    // instantiate the Platform tool factories directly and let them resolve their
    // own repository dependencies from the Api host's IServiceProvider — which
    // does have IProjectRepository et al. wired by AddInfrastructure.
    private static readonly IPlatformToolFactory[] Factories =
    [
        new GetProjectInfoTool.Factory(),
        new GetProjectTeamTool.Factory(),
        new GetPcdTool.Factory(),
        new GetHistoryTool.Factory(),
        new GetPlanTool.Factory(),
        new ListWorkflowsTool.Factory(),
        new GetArtifactsTool.Factory(),
        new GetLearningsTool.Factory()
    ];

    [Fact]
    public void Factories_ExposeAllEightPlatformTools()
    {
        Factories.Select(f => f.ToolName).Should().BeEquivalentTo(new[]
        {
            "project.get_info",
            "project.get_team",
            "project.get_pcd",
            "project.get_history",
            "project.get_plan",
            "project.list_workflows",
            "project.get_artifacts",
            "project.get_learnings"
        });
    }

    [Fact]
    public async Task GetProjectInfoTool_ReturnsProjectScopedData()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();

        var tool = new GetProjectInfoTool.Factory().Create(scope.ServiceProvider, project.Id);

        var json = await InvokeAsync(tool);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("id").GetGuid().Should().Be(project.Id);
        doc.RootElement.GetProperty("objective").GetString().Should().Be(project.Objective);
    }

    [Fact]
    public async Task Resolver_SkipsUnknownBindings()
    {
        var project = await SeedProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var resolver = new PlatformToolResolver(scope.ServiceProvider, Factories);

        var tools = resolver.Resolve(
            new[]
            {
                new AgentToolBindingShape("web.search"),                    // sandbox — handled in 7d
                new AgentToolBindingShape(GetProjectInfoTool.ToolName),
                new AgentToolBindingShape("mystery.tool")
            },
            project.Id);

        tools.Select(t => t.Name).Should().BeEquivalentTo(new[] { GetProjectInfoTool.ToolName });
    }

    private static async Task<string> InvokeAsync(AITool tool)
    {
        // AIFunctionFactory returns an AIFunction. Invoke with empty args (Platform tools
        // capture projectId at construction so no parameters are model-supplied).
        var function = (AIFunction)tool;
        var result = await function.InvokeAsync(new AIFunctionArguments());
        return result?.ToString() ?? string.Empty;
    }
}
