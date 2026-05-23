using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Api.Features.Workflows;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Workflows;

[Collection(IntegrationTestCollection.Name)]
public class WorkflowCrudTests(ApiWebApplicationFactory factory)
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

    private const string ValidNodesJson = """
        [{"id":"s","type":"Start"},{"id":"a","type":"AgentTask"},{"id":"e","type":"End"}]
        """;
    private const string ValidEdgesJson = """
        [{"from":"s","to":"a"},{"from":"a","to":"e"}]
        """;

    [Fact]
    public async Task Create_ValidGraph_Returns201()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/workflows",
            new
            {
                Name        = $"wf-{Guid.NewGuid():N}",
                Description = "happy path",
                Nodes       = ValidNodesJson,
                Edges       = ValidEdgesJson,
                CanvasState = (string?)null
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateWorkflow.Response>();
        body!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Create_InvalidGraph_Returns422()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/workflows",
            new
            {
                Name  = $"wf-bad-{Guid.NewGuid():N}",
                Nodes = "[]",          // no Start, no End
                Edges = "[]"
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Delete_AsOwner_SoftLocksDefinition()
    {
        var (client, projectId) = await SetupProject();
        var wfId = await CreateWorkflow(client, projectId);

        var del = await client.DeleteAsync($"/api/v1/projects/{projectId}/workflows/{wfId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var def = await db.WorkflowDefinitions.AsNoTracking().FirstAsync(w => w.Id == wfId);
        def.LockedAt.Should().NotBeNull("delete should soft-lock, not hard-delete");
    }

    [Fact]
    public async Task Update_LockedWorkflow_Returns400()
    {
        var (client, projectId) = await SetupProject();
        var wfId = await CreateWorkflow(client, projectId);
        await client.DeleteAsync($"/api/v1/projects/{projectId}/workflows/{wfId}");

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/projects/{projectId}/workflows/{wfId}",
            new { Description = "should fail" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_ReturnsRulesPerCause()
    {
        var (client, projectId) = await SetupProject();
        var wfId = await CreateWorkflow(client, projectId);

        var response = await client.PostAsync(
            $"/api/v1/projects/{projectId}/workflows/{wfId}/validate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ValidateWorkflow.Response>();
        body!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Run_EnqueuesAccepted202WithExecutionId()
    {
        var (client, projectId) = await SetupProject();
        var wfId = await CreateWorkflow(client, projectId);

        var response = await client.PostAsync(
            $"/api/v1/projects/{projectId}/workflows/{wfId}/run", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RunWorkflow.Response>();
        body!.ExecutionId.Should().NotBeEmpty();
        body.Status.Should().Be("Pending");
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupProject()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Wf-{Guid.NewGuid():N}",
            Objective = "Workflow test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        return (client, created!.Id);
    }

    private static async Task<Guid> CreateWorkflow(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/workflows",
            new
            {
                Name  = $"wf-{Guid.NewGuid():N}",
                Nodes = ValidNodesJson,
                Edges = ValidEdgesJson
            });
        var body = await response.Content.ReadFromJsonAsync<CreateWorkflow.Response>();
        return body!.Id;
    }
}
