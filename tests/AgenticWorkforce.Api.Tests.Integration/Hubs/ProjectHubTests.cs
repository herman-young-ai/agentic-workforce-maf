using System.Net.Http.Json;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Infrastructure.Events;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Hubs;

/// <summary>
/// End-to-end coverage for ProjectHub:
///   - members of a project successfully join and receive events
///   - non-members are blocked by the BOLA gate
///   - events published via IEventPublisher fan out to connected clients
/// </summary>
public class ProjectHubTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
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
    public async Task JoinProject_AsMember_AddsToGroupAndDeliversEvents()
    {
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, "owner@hubtest.local");

        // Create a project as owner so we have a real ProjectMember row.
        var ownerClient = _factory.CreateAuthenticatedClient(ownerId, "owner@hubtest.local", Roles.Owner);
        var createResp = await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"hub-{Guid.NewGuid():N}",
            objective = "Hub test"
        });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<CreateProject.Response>();
        var projectId = created!.Id;

        await using var connection = BuildHubConnection(ownerId, "owner@hubtest.local", Roles.Owner);

        // Bridge ProjectEvent messages onto a Task we can await on the test thread.
        var eventTcs = new TaskCompletionSource<ProjectEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ProjectEventDto>("ProjectEvent", evt => eventTcs.TrySetResult(evt));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinProject", projectId);

        // Publish via the real IEventPublisher — same path Worker would take.
        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider
                .GetRequiredService<AgenticWorkforce.Domain.Interfaces.Services.IEventPublisher>();
            await publisher.PublishAsync(new AgenticWorkforce.Domain.Entities.ProjectEvent
            {
                ProjectId = projectId,
                EventType = "task.created",
                Source    = "owner@hubtest.local",
                Severity  = AgenticWorkforce.Domain.Enums.EventSeverity.Info,
                Data      = """{"hello":"world"}"""
            });
        }

        var received = await eventTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        received.EventType.Should().Be("task.created");
        received.ProjectId.Should().Be(projectId);

        await connection.StopAsync();
    }

    [Fact]
    public async Task JoinProject_AsNonMember_ThrowsHubException()
    {
        // Owner creates the project; outsider tries to join its group.
        var ownerId = Guid.NewGuid();
        await _factory.SeedUserAsync(ownerId, "owner-bola@hubtest.local");
        var ownerClient = _factory.CreateAuthenticatedClient(ownerId, "owner-bola@hubtest.local", Roles.Owner);
        var createResp = await ownerClient.PostAsJsonAsync("/api/v1/projects", new
        {
            name      = $"bola-{Guid.NewGuid():N}",
            objective = "BOLA gate test"
        });
        createResp.EnsureSuccessStatusCode();
        var projectId = (await createResp.Content.ReadFromJsonAsync<CreateProject.Response>())!.Id;

        var outsiderId = Guid.NewGuid();
        await _factory.SeedUserAsync(outsiderId, "outsider@hubtest.local");

        await using var connection = BuildHubConnection(outsiderId, "outsider@hubtest.local", Roles.Owner);
        await connection.StartAsync();

        // Without the BOLA gate this would silently succeed — the hub MUST
        // refuse the join because the outsider has no ProjectMember row.
        var act = async () => await connection.InvokeAsync("JoinProject", projectId);
        await act.Should().ThrowAsync<HubException>();

        await connection.StopAsync();
    }

    /// <summary>
    /// Routes the SignalR client through the in-memory TestServer so the
    /// `X-Test-User-*` headers pick up our TestAuthHandler the same way
    /// every other integration test does.
    /// </summary>
    private HubConnection BuildHubConnection(Guid userId, string email, params string[] roles)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/project"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.Headers["X-Test-User-Id"]    = userId.ToString();
                options.Headers["X-Test-User-Email"] = email;
                if (roles.Length > 0)
                    options.Headers["X-Test-User-Roles"] = string.Join(",", roles);
            })
            .Build();
}
