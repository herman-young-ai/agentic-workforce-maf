using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Learnings;

/// <summary>
/// Verifies that all embedding-dependent endpoints short-circuit to HTTP 503
/// while the StubEmbeddingService is wired (Phase 4). Without this gate the
/// stub's zero-vector return would silently corrupt pgvector cosine search.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class EmbeddingStubGateTests(ApiWebApplicationFactory factory)
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
    public async Task SearchLearnings_StubEmbeddings_Returns503WithCode()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/learnings/search",
            new { Query = "anything", Limit = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("EMBEDDING_NOT_CONFIGURED");
    }

    [Fact]
    public async Task FindSimilar_StubEmbeddings_Returns503WithCode()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/learnings/{Guid.NewGuid()}/similar?limit=5");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("EMBEDDING_NOT_CONFIGURED");
    }

    [Fact]
    public async Task SearchDocuments_StubEmbeddings_Returns503WithCode()
    {
        var (client, projectId) = await SetupProject();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/documents/search",
            new { Query = "anything", Limit = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem!["code"].ToString().Should().Be("EMBEDDING_NOT_CONFIGURED");
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupProject()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Search-{Guid.NewGuid():N}",
            Objective = "Embedding gate test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        return (client, created!.Id);
    }
}
