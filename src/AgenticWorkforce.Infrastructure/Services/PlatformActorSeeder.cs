using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Ensures the platform service-account User row exists. Without it the write
/// Platform tools (run_objective, start_research, add_principle) cannot record
/// an actor and would FK-fail on the first write.
///
/// <para>Idempotent on re-run; fails fast if <see cref="PlatformActorOptions"/>
/// is unconfigured.</para>
/// </summary>
internal sealed class PlatformActorSeeder(
    IServiceScopeFactory scopes,
    IOptions<PlatformActorOptions> options,
    ILogger<PlatformActorSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (opts.UserId == Guid.Empty)
            throw new InvalidOperationException(
                "PlatformActor:UserId is required. Configure a stable UUID for the agent service account (Principle 14).");
        if (string.IsNullOrWhiteSpace(opts.Email))
            throw new InvalidOperationException(
                "PlatformActor:Email is required. The audit trail attributes agent-initiated writes to this address.");

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == opts.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            LogAlreadySeeded(logger, opts.UserId, opts.Email, null);
            return;
        }

        db.Users.Add(new User
        {
            Id               = opts.UserId,
            Email            = opts.Email,
            DisplayName      = string.IsNullOrWhiteSpace(opts.DisplayName) ? "Platform Agent" : opts.DisplayName,
            SystemRole       = SystemRole.Member,
            IsActive         = true,
            IsServiceAccount = true
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogSeeded(logger, opts.UserId, opts.Email, null);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static readonly Action<ILogger, Guid, string, Exception?> LogSeeded =
        LoggerMessage.Define<Guid, string>(LogLevel.Information,
            new EventId(1, nameof(LogSeeded)),
            "Seeded platform service-account user {UserId} ({Email}).");

    private static readonly Action<ILogger, Guid, string, Exception?> LogAlreadySeeded =
        LoggerMessage.Define<Guid, string>(LogLevel.Debug,
            new EventId(2, nameof(LogAlreadySeeded)),
            "Platform service-account user {UserId} ({Email}) already exists; skipping seed.");
}
