using AgenticWorkforce.Infrastructure.Events;

namespace AgenticWorkforce.Api.Hubs;

/// <summary>
/// Strongly-typed client surface for <see cref="ProjectHub"/>. SignalR
/// invokes these on connected clients (browser, CLI, TUI). Adding a method
/// here is the explicit contract — clients can rely on the signature
/// staying stable, and the hub gets compile-time enforcement that it only
/// sends declared messages.
/// </summary>
public interface IProjectHubClient
{
    /// <summary>
    /// A persisted <c>project_events</c> row just fired. Sent to every
    /// connection joined to the matching <c>project:{id}</c> group.
    /// </summary>
    Task ProjectEvent(ProjectEventDto evt);

    /// <summary>
    /// Server-pushed notification scoped to the connected user across all
    /// projects (sent on the <c>user:{userId:N}</c> group).
    /// </summary>
    Task Notification(NotificationDto dto);
}

/// <summary>
/// Lightweight notification payload — separate from <see cref="ProjectEventDto"/>
/// because notifications can be raised without a corresponding
/// <c>project_events</c> row (e.g. "your API key was rotated").
/// </summary>
public sealed record NotificationDto(
    string Kind,
    string Title,
    string? Body,
    DateTime CreatedAt);
