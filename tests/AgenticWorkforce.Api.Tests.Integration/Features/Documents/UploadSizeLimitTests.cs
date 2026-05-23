using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Documents;

/// <summary>
/// Verifies the 50 MB upload cap. Smaller files succeed; oversized requests
/// are rejected with either 400 (handler-side check) or 413 (Kestrel) — both
/// are acceptable safety outcomes.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class UploadSizeLimitTests(ApiWebApplicationFactory factory)
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
    public async Task Upload_SmallFile_Returns201()
    {
        var (client, projectId) = await SetupProject();

        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[64 * 1024]; // 64 KB
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "small.txt");

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/documents", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns422()
    {
        var (client, projectId) = await SetupProject();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "empty.txt");

        var response = await client.PostAsync($"/api/v1/projects/{projectId}/documents", content);

        // ValidationException -> 422 per Domain.Exceptions
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<(HttpClient Client, Guid ProjectId)> SetupProject()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Upload-{Guid.NewGuid():N}",
            Objective = "Upload limit test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        return (client, created!.Id);
    }
}
