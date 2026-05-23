namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Canonical Redis pub/sub channel names. Centralised so the publisher,
/// the relay, and every consumer agree on the prefix — changing it on
/// one side without the others would silently break delivery, since
/// pub/sub doesn't error on a no-subscriber channel.
///
/// <para><b>Naming convention</b></para>
/// <c>{aggregate}:{id:N}</c> for entity-scoped channels (the SignalR
/// backplane prepends its own <c>agentic:</c> namespace separately).
/// Always format Guids with <c>"N"</c> (32 hex chars, no dashes) for
/// readability + predictable channel matching against the patterns.
/// </summary>
public static class RedisChannels
{
    /// <summary>Prefix every project-events channel begins with.</summary>
    public const string ProjectEventsPrefix = "events:";

    /// <summary>Pattern subscription that captures every project's events.</summary>
    public const string AllProjectEventsPattern = ProjectEventsPrefix + "*";

    /// <summary>Per-project events channel used by publisher and SSE/SignalR.</summary>
    public static string ProjectEvents(Guid projectId) => $"{ProjectEventsPrefix}{projectId:N}";

    /// <summary>Per-user notification stream channel.</summary>
    public static string UserNotifications(Guid userId) => $"user:{userId:N}:notifications";

    /// <summary>
    /// Extracts the <c>{projectId:N}</c> segment from a channel produced by
    /// <see cref="ProjectEvents"/>. Returns null when <paramref name="channel"/>
    /// doesn't begin with <see cref="ProjectEventsPrefix"/>.
    /// </summary>
    public static string? TryExtractProjectIdSegment(string channel)
        => channel.StartsWith(ProjectEventsPrefix, StringComparison.Ordinal)
            ? channel[ProjectEventsPrefix.Length..]
            : null;
}
