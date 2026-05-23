using System.Net;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Admin;

/// <summary>
/// Every admin endpoint must return 403 for non-admin users. This covers the
/// platform-level role boundary — Owner/Reviewer/Operator/Viewer all see 403
/// regardless of their project membership.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AdminAuthorizationTests(ApiWebApplicationFactory factory)
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

    public static IEnumerable<object[]> AdminEndpoints() =>
    [
        ["GET",    "/api/v1/admin/dashboard/health"],
        ["GET",    "/api/v1/admin/dashboard/overview"],
        ["GET",    "/api/v1/admin/dashboard/costs?from=2026-01-01&to=2026-02-01"],
        ["GET",    "/api/v1/admin/dashboard/costs/timeline?from=2026-01-01&to=2026-02-01"],
        ["GET",    "/api/v1/admin/catalog"],
        ["GET",    "/api/v1/admin/catalog/" + Guid.NewGuid()],
        ["POST",   "/api/v1/admin/catalog/seed"],
        ["GET",    "/api/v1/admin/knowledge/learnings"],
        ["GET",    "/api/v1/admin/knowledge/promotions/pending"]
    ];

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task NonAdmin_GetsForbidden(string method, string path)
    {
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId, $"{userId}@test.local");

        // Owner is the most-privileged non-admin role and still must be denied.
        using var client = _factory.CreateAuthenticatedClient(userId, $"{userId}@test.local", Roles.Owner);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_AccessesDashboardOverview()
    {
        var adminId = Guid.NewGuid();
        await _factory.SeedUserAsync(adminId, $"{adminId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(adminId, $"{adminId}@test.local", Roles.PlatformAdmin);

        var response = await client.GetAsync("/api/v1/admin/dashboard/overview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminSeedCatalog_AsAdmin_Returns501NotImplemented()
    {
        var adminId = Guid.NewGuid();
        await _factory.SeedUserAsync(adminId, $"{adminId}@test.local");
        using var client = _factory.CreateAuthenticatedClient(adminId, $"{adminId}@test.local", Roles.PlatformAdmin);

        var response = await client.PostAsync("/api/v1/admin/catalog/seed", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }
}
