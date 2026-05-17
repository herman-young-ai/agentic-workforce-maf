using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Publishes project events to subscribers (UI streams, audit pipelines).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(ProjectEvent evt, CancellationToken ct = default);

    Task PublishAsync(
        string channel,
        string eventType,
        object data,
        CancellationToken ct = default);
}
