using System.Threading.Channels;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Services;

/// <summary>
/// Drains the bounded <c>Channel&lt;LlmCall&gt;</c> populated by
/// <see cref="Middleware.CostTrackingChatClient"/> into PostgreSQL via
/// <see cref="ILlmCallRepository"/>. Batches up to
/// <see cref="AgentRuntimeOptions.LlmCallDrainBatchSize"/> records or
/// <see cref="AgentRuntimeOptions.LlmCallDrainFlushInterval"/> (whichever first).
/// </summary>
internal sealed class LlmCallDrainService(
    ChannelReader<LlmCall> reader,
    IServiceScopeFactory scopes,
    IOptions<AgentRuntimeOptions> options,
    ILogger<LlmCallDrainService> logger) : BackgroundService
{
    private readonly int _batchSize = options.Value.LlmCallDrainBatchSize;
    private readonly TimeSpan _flushInterval = options.Value.LlmCallDrainFlushInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<LlmCall>(capacity: _batchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            buffer.Clear();
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            flushCts.CancelAfter(_flushInterval);

            try
            {
                while (buffer.Count < _batchSize)
                {
                    var call = await reader.ReadAsync(flushCts.Token).ConfigureAwait(false);
                    buffer.Add(call);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Flush interval elapsed — drain whatever we collected.
            }
            catch (ChannelClosedException)
            {
                break;
            }

            if (buffer.Count == 0) continue;

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILlmCallRepository>();
                await repo.AddBatchAsync(buffer, stoppingToken).ConfigureAwait(false);
                logger.LogDebug("Drained {Count} LlmCall records.", buffer.Count);
            }
#pragma warning disable CA1031 // Drain service must not crash on transient persistence errors; surface via metrics in later phases.
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist batch of {Count} LlmCall records; will retry next interval.", buffer.Count);
            }
#pragma warning restore CA1031
        }
    }
}
