using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Context;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Context;

[Collection(IntegrationTestCollection.Name)]
public class ContextCrudTests(ApiWebApplicationFactory factory)
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
    public async Task Get_FreshProject_AutoProvisionsContext()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/context");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetContext.Response>();
        body!.ContextVersion.Should().Be(1);
    }

    [Fact]
    public async Task AddPrinciple_BumpsContextVersion()
    {
        var (client, projectId) = await SetupProject();
        // Initialize the context
        await client.GetAsync($"/api/v1/projects/{projectId}/context");

        var add = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/context/principles",
            new { Text = "Always use async/await." });

        add.StatusCode.Should().Be(HttpStatusCode.Created);

        var get = await client.GetAsync($"/api/v1/projects/{projectId}/context");
        var body = await get.Content.ReadFromJsonAsync<GetContext.Response>();
        body!.ContextVersion.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task AddPrinciple_AppendsChangeHistory()
    {
        var (client, projectId) = await SetupProject();
        await client.GetAsync($"/api/v1/projects/{projectId}/context");
        await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/context/principles",
            new { Text = "Audit trail required." });

        var history = await client.GetAsync($"/api/v1/projects/{projectId}/context/history");
        history.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await history.Content.ReadFromJsonAsync<List<GetContextHistory.Response>>();
        entries!.Should().NotBeEmpty();
        entries[0].Path.Should().StartWith("principles.");
    }

    [Fact]
    public async Task RemovePrinciple_NonexistentId_Returns404()
    {
        var (client, projectId) = await SetupProject();
        await client.GetAsync($"/api/v1/projects/{projectId}/context");

        var del = await client.DeleteAsync(
            $"/api/v1/projects/{projectId}/context/principles/does-not-exist");

        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupProject()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Ctx-{Guid.NewGuid():N}",
            Objective = "Context test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        return (client, created!.Id);
    }
}
