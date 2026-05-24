using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration.Services;

/// <summary>
/// Phase 7a verification: <see cref="AgentSeedService"/> against the Testcontainers
/// PostgreSQL. Uses a fixture <see cref="IAgentSeedSource"/> so we control the
/// inputs precisely — the embedded-YAML path is exercised separately by 7b's
/// architecture tests.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class AgentSeedServiceTests(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private readonly ApiWebApplicationFactory _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AgentSeedService BuildSut(IAgentSeedSource source) =>
        new(
            scopes:  _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            source:  source,
            logger:  _factory.Services.GetRequiredService<ILoggerFactory>()
                        .CreateLogger<AgentSeedService>());

    private static AgentSeedDefinition Def(string name, string version, string? prompt = null) => new()
    {
        AgentName    = name,
        AgentType    = "system",
        AgentVersion = version,
        SystemPrompt = prompt,
        Visibility   = "Internal"
    };

    private async Task<AgentCatalog?> FindAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentCatalogs.AsNoTracking().FirstOrDefaultAsync(a => a.AgentName == name);
    }

    [Fact]
    public async Task StartAsync_FirstRun_InsertsAgent()
    {
        var name = $"seedtest.first-{Guid.NewGuid():N}";
        var sut = BuildSut(new FixtureSource(Def(name, "1.0.0", "prompt v1")));

        await sut.StartAsync(CancellationToken.None);

        var row = await FindAsync(name);
        row.Should().NotBeNull();
        row!.AgentVersion.Should().Be("1.0.0");
        row.SystemPrompt.Should().Be("prompt v1");
    }

    [Fact]
    public async Task StartAsync_SecondRun_IsNoOp_WhenVersionUnchanged()
    {
        var name = $"seedtest.noop-{Guid.NewGuid():N}";
        var source = new FixtureSource(Def(name, "1.0.0", "p"));

        await BuildSut(source).StartAsync(CancellationToken.None);
        var beforeUpdatedAt = (await FindAsync(name))!.UpdatedAt;

        await Task.Delay(10);
        await BuildSut(source).StartAsync(CancellationToken.None);

        var after = (await FindAsync(name))!;
        after.UpdatedAt.Should().Be(beforeUpdatedAt, "no update path runs when version is equal");
    }

    [Fact]
    public async Task StartAsync_BumpedVersion_UpdatesRowAndAppendsPromptVersion()
    {
        var name = $"seedtest.bump-{Guid.NewGuid():N}";
        await BuildSut(new FixtureSource(Def(name, "1.0.0", "prompt v1"))).StartAsync(CancellationToken.None);

        await BuildSut(new FixtureSource(Def(name, "1.1.0", "prompt v2"))).StartAsync(CancellationToken.None);

        var row = (await FindAsync(name))!;
        row.AgentVersion.Should().Be("1.1.0");
        row.SystemPrompt.Should().Be("prompt v2");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var history = await db.PromptVersions
            .Where(p => p.EntityType == nameof(AgentCatalog) && p.EntityId == row.Id)
            .ToListAsync();
        history.Should().HaveCount(1, "the previous prompt body is archived when system_prompt changes");
        history[0].Content.Should().Be("prompt v1");
    }

    [Fact]
    public async Task StartAsync_OlderVersion_IsIgnored()
    {
        var name = $"seedtest.older-{Guid.NewGuid():N}";
        await BuildSut(new FixtureSource(Def(name, "2.0.0", "newer"))).StartAsync(CancellationToken.None);

        await BuildSut(new FixtureSource(Def(name, "1.9.9", "older"))).StartAsync(CancellationToken.None);

        var row = (await FindAsync(name))!;
        row.AgentVersion.Should().Be("2.0.0");
        row.SystemPrompt.Should().Be("newer");
    }

    [Fact]
    public async Task StartAsync_MalformedVersion_Throws()
    {
        var name = $"seedtest.bad-{Guid.NewGuid():N}";
        var sut = BuildSut(new FixtureSource(Def(name, "not-a-version")));

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>().WithMessage("*not-a-version*");
    }

    private sealed class FixtureSource(params AgentSeedDefinition[] defs) : IAgentSeedSource
    {
        public IReadOnlyList<AgentSeedDefinition> Load() => defs;
    }
}
