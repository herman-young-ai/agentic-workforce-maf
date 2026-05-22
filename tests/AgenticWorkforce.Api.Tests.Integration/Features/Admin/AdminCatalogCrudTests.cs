using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Admin.Catalog;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Admin;

public class AdminCatalogCrudTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
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
    public async Task Create_DuplicateName_Returns409()
    {
        var client = await AdminClient();
        var name = $"agent-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/v1/admin/catalog", new { AgentName = name });
        var second = await client.PostAsJsonAsync("/api/v1/admin/catalog", new { AgentName = name });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdatePrompt_VersionsHistory()
    {
        var client = await AdminClient();
        var name = $"agent-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync("/api/v1/admin/catalog", new { AgentName = name });
        var body = await created.Content.ReadFromJsonAsync<AdminCreateAgent.Response>();
        var agentId = body!.Id;

        var firstPrompt = await client.PutAsJsonAsync(
            $"/api/v1/admin/catalog/{agentId}/prompt",
            new { SystemPrompt = "you are a helpful agent", ChangeReason = "initial" });
        var firstBody = await firstPrompt.Content.ReadFromJsonAsync<AdminUpdatePrompt.Response>();

        var secondPrompt = await client.PutAsJsonAsync(
            $"/api/v1/admin/catalog/{agentId}/prompt",
            new { SystemPrompt = "you are a refined agent", ChangeReason = "refinement" });
        var secondBody = await secondPrompt.Content.ReadFromJsonAsync<AdminUpdatePrompt.Response>();

        firstBody!.Version.Should().Be(1);
        secondBody!.Version.Should().Be(2);

        var history = await client.GetAsync($"/api/v1/admin/catalog/{agentId}/prompt-history");
        var versions = await history.Content.ReadFromJsonAsync<List<AdminPromptHistory.Response>>();
        versions!.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnableDisable_TogglesFlag()
    {
        var client = await AdminClient();
        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/catalog", new { AgentName = $"agent-{Guid.NewGuid():N}" });
        var body = await created.Content.ReadFromJsonAsync<AdminCreateAgent.Response>();
        var agentId = body!.Id;

        var disable = await client.PostAsync($"/api/v1/admin/catalog/{agentId}/disable", null);
        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.AgentCatalogs.AsNoTracking().FirstAsync(a => a.Id == agentId);
            agent.Enabled.Should().BeFalse();
        }

        var enable = await client.PostAsync($"/api/v1/admin/catalog/{agentId}/enable", null);
        enable.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.AgentCatalogs.AsNoTracking().FirstAsync(a => a.Id == agentId);
            agent.Enabled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Delete_AsAdmin_SoftDisablesAgent()
    {
        var client = await AdminClient();
        var created = await client.PostAsJsonAsync(
            "/api/v1/admin/catalog", new { AgentName = $"agent-{Guid.NewGuid():N}" });
        var body = await created.Content.ReadFromJsonAsync<AdminCreateAgent.Response>();
        var agentId = body!.Id;

        var del = await client.DeleteAsync($"/api/v1/admin/catalog/{agentId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.AgentCatalogs.AsNoTracking().FirstAsync(a => a.Id == agentId);
        agent.Enabled.Should().BeFalse("delete should soft-disable, not hard-remove");
    }

    private async Task<HttpClient> AdminClient()
    {
        var adminId = Guid.NewGuid();
        await _factory.SeedUserAsync(adminId, $"{adminId}@test.local", SystemRole.PlatformAdmin);
        return _factory.CreateAuthenticatedClient(
            adminId, $"{adminId}@test.local", Roles.PlatformAdmin);
    }
}
