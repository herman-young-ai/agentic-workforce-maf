namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Configuration for the platform service-account actor. Bound from
/// configuration section <c>PlatformActor</c>. Both fields are required —
/// the seeder fails fast if either is missing (Principle 14: missing config
/// means denied).
/// </summary>
public sealed class PlatformActorOptions
{
    public const string SectionName = "PlatformActor";

    /// <summary>UUID under which agent-initiated writes are recorded.</summary>
    public Guid UserId { get; set; }

    /// <summary>Email associated with the service-account user row.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name; used only on first-seed and Audit log emission.</summary>
    public string DisplayName { get; set; } = "Platform Agent";
}
