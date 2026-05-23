using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthorizationTests(ApiWebApplicationFactory factory)
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

    // -------------------------------------------------------------------------
    // Authentication: 401 when no credentials supplied
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_WithoutAuthHeader_Returns401()
    {
        using var client   = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjects_WithoutAuthHeader_Returns401()
    {
        using var client   = _factory.CreateClient();
        var projectId = Guid.NewGuid();
        var response  = await client.GetAsync($"/api/v1/projects/{projectId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // Authentication: 200 with valid test credentials
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_WithValidAuth_Returns200()
    {
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId, $"{userId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(userId, $"{userId}@test.local", Roles.Viewer);

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetMe.Response>();
        body!.Id.Should().Be(userId);
    }

    // -------------------------------------------------------------------------
    // BOLA: authenticated but not a project member → 403
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProject_AuthenticatedNonMember_Returns403()
    {
        // Owner creates project
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var ownerClient = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Auth-BOLA-{Guid.NewGuid():N}",
            Objective = "BOLA test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        // Authenticated user with Viewer JWT role but no project membership
        var intruderId = Guid.NewGuid();
        await _factory.SeedUserAsync(intruderId, $"{intruderId}@test.local");
        using var intruderClient = _factory.CreateAuthenticatedClient(intruderId, $"{intruderId}@test.local", Roles.Viewer);

        var response = await intruderClient.GetAsync($"/api/v1/projects/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------
    // Role hierarchy: Viewer JWT role cannot reach Owner-only endpoint → 403
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchProject_ViewerRoleLacksOwnerPermission_Returns403()
    {
        // Owner creates project
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var ownerClient = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"RoleHier-{Guid.NewGuid():N}",
            Objective = "Role hierarchy test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        // Add a second user as Viewer on the project
        var viewerId = Guid.NewGuid();
        await _factory.SeedUserAsync(viewerId, $"{viewerId}@test.local");
        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{created!.Id}/members", new
        {
            UserId = viewerId,
            Role   = ProjectRole.Viewer
        });

        // Viewer tries to PATCH (Owner-only operation)
        using var viewerClient = _factory.CreateAuthenticatedClient(viewerId, $"{viewerId}@test.local", Roles.Viewer);
        var response = await viewerClient.PatchAsJsonAsync(
            $"/api/v1/projects/{created.Id}", new { Name = "Hijacked Name" });

        // RequireOwner policy rejects Viewer JWT role at the middleware layer
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ArchiveProject_OperatorRoleLacksOwnerPermission_Returns403()
    {
        var ownerId    = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId,    $"{ownerId}@test.local");
        await _factory.SeedUserAsync(operatorId, $"{operatorId}@test.local");

        using var ownerClient = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);
        var created = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"OpArchive-{Guid.NewGuid():N}",
            Objective = "Operator archive test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        await ownerClient.PostAsJsonAsync($"/api/v1/projects/{created!.Id}/members", new
        {
            UserId = operatorId,
            Role   = ProjectRole.Operator
        });

        using var operatorClient = _factory.CreateAuthenticatedClient(operatorId, $"{operatorId}@test.local", Roles.Operator);
        var response = await operatorClient.PostAsync($"/api/v1/projects/{created.Id}/archive", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
