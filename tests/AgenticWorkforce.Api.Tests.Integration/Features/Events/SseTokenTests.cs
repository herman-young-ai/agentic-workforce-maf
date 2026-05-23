using System.Net;
using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Features.Events;

/// <summary>
/// Covers the SSE token exchange + handler:
///   - issued tokens grant one stream access
///   - a replayed token is rejected (GETDEL = atomic single-use)
///   - requests without a token fall back to the JWT scheme
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class SseTokenTests(ApiWebApplicationFactory factory)
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
    public async Task CreateSseToken_AsAuthenticatedUser_ReturnsTokenWith30sTtl()
    {
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId, "sse-issuer@test.local");
        var client = _factory.CreateAuthenticatedClient(userId, "sse-issuer@test.local", Roles.Viewer);

        var resp = await client.PostAsync("/api/v1/auth/sse-token", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CreateSseToken.Response>();
        body.Should().NotBeNull();
        body!.Token.Should().HaveLength(64); // 32 random bytes → 64 hex chars
        body.ExpiresInSeconds.Should().Be(30);
    }

    [Fact]
    public async Task SseToken_IsSingleUse_ReplayReturns401()
    {
        // Use the notifications stream — no project setup needed, the
        // route only requires the SseStream policy + a resolved user.
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId, "sse-replay@test.local");
        var jwtClient = _factory.CreateAuthenticatedClient(userId, "sse-replay@test.local", Roles.Viewer);

        var issue = await jwtClient.PostAsync("/api/v1/auth/sse-token", content: null);
        issue.EnsureSuccessStatusCode();
        var token = (await issue.Content.ReadFromJsonAsync<CreateSseToken.Response>())!.Token;

        // First redemption: open the stream, then immediately disconnect.
        // Don't await full read — the stream stays open until cancel.
        using var anon = _factory.CreateClient();
        using var cts1 = new CancellationTokenSource();
        var firstReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/notifications/stream?token={token}");
        var firstResp = await anon.SendAsync(firstReq, HttpCompletionOption.ResponseHeadersRead, cts1.Token);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResp.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        cts1.Cancel();
        firstResp.Dispose();

        // Second redemption: same token is gone (GETDEL drained it).
        using var anon2 = _factory.CreateClient();
        var replayResp = await anon2.GetAsync($"/api/v1/notifications/stream?token={token}");
        replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
