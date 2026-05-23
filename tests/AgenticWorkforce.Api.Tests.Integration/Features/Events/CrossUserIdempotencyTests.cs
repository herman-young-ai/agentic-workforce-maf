using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Events;

/// <summary>
/// Phase 3.5 added userId-scoped idempotency to close a cross-user replay
/// vulnerability — user B must not be able to redeem user A's cached
/// response by sending the same X-Idempotency-Key header. Phase 5 swaps
/// the in-memory store for Redis, so this test confirms the scoping
/// survives the swap.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CrossUserIdempotencyTests(ApiWebApplicationFactory factory)
    : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<AgenticWorkforce.Infrastructure.Data.AppDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SameIdempotencyKey_DifferentUsers_AreIsolated()
    {
        var sharedKey = $"shared-{Guid.NewGuid():N}";

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await _factory.SeedUserAsync(userA, "alice@idem.local");
        await _factory.SeedUserAsync(userB, "bob@idem.local");

        var clientA = _factory.CreateAuthenticatedClient(userA, "alice@idem.local", Roles.Owner);
        var clientB = _factory.CreateAuthenticatedClient(userB, "bob@idem.local", Roles.Owner);

        // Alice creates a project with the idempotency key.
        clientA.DefaultRequestHeaders.Add("X-Idempotency-Key", sharedKey);
        var respA = await clientA.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"alice-{Guid.NewGuid():N}",
            objective = "Alice's project"
        });
        respA.StatusCode.Should().Be(HttpStatusCode.Created);
        var projectA = (await respA.Content.ReadFromJsonAsync<CreateProject.Response>())!;

        // Bob submits the same idempotency key with a different project
        // name. If user-scoping is preserved, Bob gets his own NEW project,
        // not Alice's cached response. If user-scoping has regressed,
        // Bob would receive Alice's projectId.
        clientB.DefaultRequestHeaders.Add("X-Idempotency-Key", sharedKey);
        var respB = await clientB.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"bob-{Guid.NewGuid():N}",
            objective = "Bob's project"
        });
        respB.StatusCode.Should().Be(HttpStatusCode.Created);
        var projectB = (await respB.Content.ReadFromJsonAsync<CreateProject.Response>())!;

        projectB.Id.Should().NotBe(projectA.Id,
            "Bob must not receive Alice's cached idempotency response (Principle: user-scoped keys).");
        projectB.Name.Should().StartWith("bob-");
    }

    /// <summary>
    /// Atomic claim: when the same user fires two requests with the same
    /// idempotency key concurrently, only ONE wins the claim and creates
    /// the resource. The other surfaces a 409 — by the time it retries,
    /// the first request's response is in the cache.
    /// </summary>
    [Fact]
    public async Task SameUser_SameKey_Concurrent_OneSucceedsOneGets409()
    {
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId, "concurrent@idem.local");
        var sharedKey = $"concurrent-{Guid.NewGuid():N}";

        // Fire two simultaneous requests sharing the user AND the key.
        // Pre-fix, both would have missed the cache, both would have
        // created a project, both would have returned 201. The atomic
        // claim makes one win and the other surface as 409.
        var firstPost  = SendCreateProjectAsync(userId, sharedKey, "first");
        var secondPost = SendCreateProjectAsync(userId, sharedKey, "second");

        var responses = await Task.WhenAll(firstPost, secondPost);

        // Exactly one request should create the resource and the other
        // should be rejected as a concurrent claim.
        var statuses = responses.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();
        statuses.Should().Equal([201, 409]);
    }

    private async Task<HttpResponseMessage> SendCreateProjectAsync(
        Guid userId, string idempotencyKey, string namePrefix)
    {
        var client = _factory.CreateAuthenticatedClient(userId, "concurrent@idem.local", Roles.Owner);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", idempotencyKey);
        return await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"{namePrefix}-{Guid.NewGuid():N}",
            objective = $"{namePrefix} project"
        });
    }
}
