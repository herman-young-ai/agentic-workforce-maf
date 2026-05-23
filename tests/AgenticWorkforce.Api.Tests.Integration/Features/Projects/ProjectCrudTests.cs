using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Projects;

[Collection(IntegrationTestCollection.Name)]
public class ProjectCrudTests(ApiWebApplicationFactory factory)
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
    // POST /api/v1/projects
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateProject_ValidRequest_Returns201WithLocation()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var response = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Proj-{Guid.NewGuid():N}",
            Objective = "Integration test objective"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var body = await response.Content.ReadFromJsonAsync<CreateProject.Response>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Status.Should().Be(ProjectStatus.Active);
    }

    [Fact]
    public async Task CreateProject_DuplicateName_Returns409()
    {
        var ownerId = Guid.NewGuid();
        var name    = $"Dup-{Guid.NewGuid():N}";
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        await client.PostAsJsonAsync("/api/v1/projects", new { Name = name, Objective = "First" });
        var second = await client.PostAsJsonAsync("/api/v1/projects", new { Name = name, Objective = "Second" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateProject_IdempotencyKey_ReturnsSameId()
    {
        var ownerId       = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);

        var first  = await client.PostAsJsonAsync("/api/v1/projects", new { Name = $"Idem-{Guid.NewGuid():N}", Objective = "x" });
        var second = await client.PostAsJsonAsync("/api/v1/projects", new { Name = $"Idem-{Guid.NewGuid():N}", Objective = "x" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var r1 = await first.Content.ReadFromJsonAsync<CreateProject.Response>();
        var r2 = await second.Content.ReadFromJsonAsync<CreateProject.Response>();
        r2!.Id.Should().Be(r1!.Id);
    }

    // -------------------------------------------------------------------------
    // GET /api/v1/projects/{projectId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProject_AsOwner_Returns200WithMembers()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Get-{Guid.NewGuid():N}",
            Objective = "Get test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        var response = await client.GetAsync($"/api/v1/projects/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetProject.Response>();
        body!.Id.Should().Be(created.Id);
        body.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProject_AsNonMember_Returns403()
    {
        // Owner creates project
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var ownerClient = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"BOLA-{Guid.NewGuid():N}",
            Objective = "BOLA test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        // Different user (Viewer role in JWT, but not a project member) tries to read
        var intruderId = Guid.NewGuid();
        await _factory.SeedUserAsync(intruderId, $"{intruderId}@test.local");
        using var intruderClient = _factory.CreateAuthenticatedClient(intruderId, $"{intruderId}@test.local", Roles.Viewer);

        var response = await intruderClient.GetAsync($"/api/v1/projects/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------
    // PATCH /api/v1/projects/{projectId}
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateProject_AsOwner_Returns200WithUpdatedName()
    {
        var ownerId  = Guid.NewGuid();
        var newName  = $"Updated-{Guid.NewGuid():N}";
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Before-{Guid.NewGuid():N}",
            Objective = "Update test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        var patchResponse = await client.PatchAsJsonAsync($"/api/v1/projects/{created!.Id}", new { Name = newName });

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patchResponse.Content.ReadFromJsonAsync<UpdateProject.Response>();
        body!.Name.Should().Be(newName);
    }

    // -------------------------------------------------------------------------
    // POST /api/v1/projects/{projectId}/archive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ArchiveProject_AsOwner_Returns204()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"Archive-{Guid.NewGuid():N}",
            Objective = "Archive test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        var archiveResponse = await client.PostAsync($"/api/v1/projects/{created!.Id}/archive", null);

        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm status in DB
        using var scope = _factory.Services.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == created.Id);
        project.Status.Should().Be(ProjectStatus.Archived);
    }

    [Fact]
    public async Task ArchiveProject_AlreadyArchived_Returns204Idempotent()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, $"{ownerId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(ownerId, $"{ownerId}@test.local", Roles.Owner);

        var created = await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            Name      = $"ArchIdem-{Guid.NewGuid():N}",
            Objective = "Idempotent archive test"
        })).Content.ReadFromJsonAsync<CreateProject.Response>();

        await client.PostAsync($"/api/v1/projects/{created!.Id}/archive", null);
        var second = await client.PostAsync($"/api/v1/projects/{created.Id}/archive", null);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
