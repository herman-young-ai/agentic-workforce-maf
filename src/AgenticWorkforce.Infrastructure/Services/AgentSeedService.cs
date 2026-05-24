using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Idempotent agent catalog seeder. On <see cref="StartAsync"/>:
/// <list type="number">
///   <item>Loads every <see cref="AgentSeedDefinition"/> from the configured
///         <see cref="IAgentSeedSource"/>.</item>
///   <item>Reads all existing <see cref="AgentCatalog"/> rows in one round trip.</item>
///   <item>For each YAML: inserts when missing; updates when YAML version is
///         strictly greater than DB version; skips when equal-or-older.</item>
///   <item>When updating, appends a <see cref="PromptVersion"/> row if
///         <c>system_prompt</c> changed (Principle 20 — version everything;
///         in-flight runs hold their own prompt revision pointer).</item>
///   <item>Evicts the matching <see cref="CachingAgentCatalogRepository"/> keys
///         after every write.</item>
/// </list>
///
/// <para><b>Failure policy</b></para>
/// Bad YAML (missing required field, malformed <c>agent_version</c>, invalid
/// visibility) throws and the host fails to start. There is no
/// "best-effort skip on broken YAML" — the catalog is a security-critical
/// contract (Principle 8).
/// </summary>
internal sealed class AgentSeedService(
    IServiceScopeFactory scopes,
    IAgentSeedSource source,
    ILogger<AgentSeedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAgentCatalogRepository>();
        var prompts = scope.ServiceProvider.GetRequiredService<IPromptVersionRepository>();
        var cache   = scope.ServiceProvider.GetRequiredService<IMemoryCache>();

        var existing = (await catalog.ListAllAsync(cancellationToken))
            .ToDictionary(a => a.AgentName, StringComparer.Ordinal);

        var definitions = source.Load();
        var seeded = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var def in definitions)
        {
            var incoming = AgentSemver.Parse(def.AgentVersion);

            if (!existing.TryGetValue(def.AgentName, out var current))
            {
                await catalog.AddAsync(AgentSeedMapper.ToEntity(def), cancellationToken);
                LogSeeded(logger, def.AgentName, def.AgentVersion, null);
                seeded++;
                continue;
            }

            var currentVersion = AgentSemver.Parse(current.AgentVersion!);
            if (incoming.CompareTo(currentVersion) <= 0)
            {
                skipped++;
                continue;
            }

            var promptChanged = !string.Equals(current.SystemPrompt, def.SystemPrompt, StringComparison.Ordinal);
            if (promptChanged)
            {
                var nextVersion = await prompts.GetCurrentVersionAsync(
                    entityType: nameof(AgentCatalog),
                    entityId:   current.Id,
                    cancellationToken) + 1;

                await prompts.AddAsync(new PromptVersion
                {
                    EntityType   = nameof(AgentCatalog),
                    EntityId     = current.Id,
                    PromptType   = "system",
                    Content      = current.SystemPrompt ?? string.Empty,
                    Version      = nextVersion,
                    ChangedBy    = "platform:agent-seed",
                    ChangeReason = $"Seeded version bump {currentVersion} -> {incoming}"
                }, cancellationToken);
            }

            AgentSeedMapper.Update(current, def);
            await catalog.UpdateAsync(current, cancellationToken);

            // The decorator already evicts on UpdateAsync, but the same IMemoryCache backs
            // both decorator instances if the host has multiple scopes — explicit eviction
            // is cheap insurance.
            cache.Remove($"agent:id:{current.Id}");
            cache.Remove($"agent:name:{current.AgentName}");

            LogUpdated(logger, def.AgentName, current.AgentVersion!, def.AgentVersion, null);
            updated++;
        }

        LogSummary(logger, seeded, updated, skipped, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static readonly Action<ILogger, string, string, Exception?> LogSeeded =
        LoggerMessage.Define<string, string>(LogLevel.Information,
            new EventId(1, nameof(LogSeeded)),
            "Seeded agent {AgentName} v{Version}");

    private static readonly Action<ILogger, string, string, string, Exception?> LogUpdated =
        LoggerMessage.Define<string, string, string>(LogLevel.Information,
            new EventId(2, nameof(LogUpdated)),
            "Updated agent {AgentName} v{FromVersion} -> v{ToVersion}");

    private static readonly Action<ILogger, int, int, int, Exception?> LogSummary =
        LoggerMessage.Define<int, int, int>(LogLevel.Information,
            new EventId(3, nameof(LogSummary)),
            "AgentSeedService completed: {Seeded} seeded, {Updated} updated, {Skipped} skipped.");
}
