using System.Text.Json;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Infrastructure.Events;

/// <summary>
/// Constructs <see cref="ProjectEvent"/> rows in one line so endpoint
/// handlers don't repeat the same 6-line block per event emission.
///
/// <para><b>Why this exists</b></para>
/// Phase-4 mutating handlers now publish a <c>ProjectEvent</c> on every
/// successful state change (project lifecycle, task lifecycle, knowledge
/// retraction, …). Before this builder, each handler constructed the
/// entity inline — ten near-identical blocks of <c>ProjectId / TaskId /
/// EventType / Source / Severity / Data</c>. The duplication risked
/// drift in two specific ways:
/// <list type="bullet">
///   <item>
///     Inline <c>JsonSerializer.Serialize(data)</c> used default
///     System.Text.Json options (PascalCase). The rest of the wire uses
///     <see cref="WireJsonOptions.Default"/> (camelCase). Routing every
///     data payload through this helper guarantees a single JSON shape
///     across REST responses, SignalR/SSE frames, and the inner
///     <c>Data</c> string.
///   </item>
///   <item>
///     Call-sites tended to duplicate the foreign-key value in the
///     <c>Data</c> payload (e.g. <c>new { task.Id, task.Objective }</c>
///     when <c>TaskId</c> was already a typed column). The factory
///     methods bind the FKs explicitly and document that the
///     <paramref name="data"/> argument should carry only the operation-
///     specific fields, not the entity ids the row already records.
///   </item>
/// </list>
/// </summary>
public static class ProjectEventBuilder
{
    /// <summary>
    /// Builds a project-scoped event. Use this for project-lifecycle
    /// events (created, paused, …) and for events whose primary subject
    /// is the project itself (learning retracted, context updated, …).
    /// </summary>
    public static ProjectEvent ForProject(
        Guid projectId,
        string eventType,
        string sourceEmail,
        object data,
        EventSeverity severity = EventSeverity.Info)
        => new()
        {
            ProjectId = projectId,
            EventType = eventType,
            Source    = sourceEmail,
            Severity  = severity,
            Data      = JsonSerializer.Serialize(data, WireJsonOptions.Default)
        };

    /// <summary>
    /// Builds a task-scoped event. Sets both the <see cref="ProjectEvent.ProjectId"/>
    /// and <see cref="ProjectEvent.TaskId"/> columns so callers don't
    /// need to duplicate either in the <paramref name="data"/> payload.
    /// </summary>
    public static ProjectEvent ForTask(
        Guid projectId,
        Guid taskId,
        string eventType,
        string sourceEmail,
        object data,
        EventSeverity severity = EventSeverity.Info)
        => new()
        {
            ProjectId = projectId,
            TaskId    = taskId,
            EventType = eventType,
            Source    = sourceEmail,
            Severity  = severity,
            Data      = JsonSerializer.Serialize(data, WireJsonOptions.Default)
        };
}
