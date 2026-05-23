using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Catalog;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Catalog;

[Collection(IntegrationTestCollection.Name)]
public class CatalogBrowseTests(ApiWebApplicationFactory factory)
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
    public async Task List_Member_OnlySeesPublicAgents()
    {
        var publicId  = await SeedAgent("public-agent", AgentVisibility.Public);
        var internalId = await SeedAgent("internal-agent", AgentVisibility.Internal);
        var privateId  = await SeedAgent("private-agent", AgentVisibility.Private);

        var memberId = Guid.NewGuid();
        await _factory.SeedUserAsync(memberId, $"{memberId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(memberId, $"{memberId}@test.local");

        var response = await client.GetAsync("/api/v1/catalog");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<Domain.Pagination.PagedResult<ListCatalog.Response>>();
        var ids = page!.Items.Select(i => i.Id).ToList();

        ids.Should().Contain(publicId);
        ids.Should().NotContain(internalId);
        ids.Should().NotContain(privateId);
    }

    [Fact]
    public async Task GetById_PrivateAgent_AsMember_Returns404()
    {
        var privateId = await SeedAgent("private-only", AgentVisibility.Private);
        var memberId = Guid.NewGuid();
        await _factory.SeedUserAsync(memberId, $"{memberId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(memberId, $"{memberId}@test.local");

        var response = await client.GetAsync($"/api/v1/catalog/{privateId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> SeedAgent(string name, AgentVisibility visibility)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = new AgentCatalog
        {
            AgentName  = $"{name}-{Guid.NewGuid():N}",
            Enabled    = true,
            Visibility = visibility,
            Keywords   = []
        };
        db.AgentCatalogs.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }
}
