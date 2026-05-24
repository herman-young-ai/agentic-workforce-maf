using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Configuration-bound <see cref="IPlatformActor"/>. The
/// <see cref="PlatformActorSeeder"/> ensures the underlying User row exists at
/// host startup.
/// </summary>
internal sealed class PlatformActor(IOptions<PlatformActorOptions> options) : IPlatformActor
{
    private readonly PlatformActorOptions _opts = options.Value;

    public Guid UserId => _opts.UserId;
    public string Email => _opts.Email;
}
