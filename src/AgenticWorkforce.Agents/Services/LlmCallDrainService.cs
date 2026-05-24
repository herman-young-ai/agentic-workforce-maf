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
///
/// <para><b>Failure policy (Principle 8: fail fast, never degrade silently)</b></para>
/// <c>LlmCall</c> rows are the system of record for cost and budget
/// (<c>BudgetService.GetStatusAsync</c> sums them). Silently dropping a
/// batch corrupts the budget guard. On a persistence error this service
/// retries with bounded exponential backoff
/// (<see cref="AgentRuntimeOptions.LlmCallDrainMaxRetries"/> attempts,
/// starting at <see cref="AgentRuntimeOptions.LlmCallDrainRetryBaseDelay"/>).
/// If every retry fails the exception is rethrown so the host process
/// crashes — operators must restore persistence before agents can run
/// again. There is no silent-drop path.
/// </summary>
internal sealed class LlmCallDrainService(
    ChannelReader<LlmCall> reader,
    IServiceScopeFactory scopes,
    IOptions<AgentRuntimeOptions> options,
    ILogger<LlmCallDrainService> logger) : BackgroundService
{
    private readonly int _batchSize = options.Value.LlmCallDrainBatchSize;
    private readonly TimeSpan _flushInterval = options.Value.LlmCallDrainFlushInterval;
    private readonly int _maxRetries = options.Value.LlmCallDrainMaxRetries;
    private readonly TimeSpan _retryBaseDelay = options.Value.LlmCallDrainRetryBaseDelay;

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

            await PersistBatchOrThrowAsync(buffer, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PersistBatchOrThrowAsync(List<LlmCall> batch, CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILlmCallRepository>();
                await repo.AddBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                LogDrained(logger, batch.Count, null);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown in flight — bubble the cancellation; partial drain is acceptable on graceful stop.
                throw;
            }
            catch (Exception ex) when (attempt < _maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(_retryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                LogRetry(logger, attempt, _maxRetries, batch.Count, delay, ex);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogExhausted(logger, _maxRetries, batch.Count, ex);
                // Principle 8: rethrow so the host crashes. LlmCall is the source of truth
                // for spend; a silent drop here would corrupt every BudgetService check.
                throw;
            }
        }
    }

    private static readonly Action<ILogger, int, Exception?> LogDrained =
        LoggerMessage.Define<int>(LogLevel.Debug,
            new EventId(1, nameof(LogDrained)),
            "Drained {Count} LlmCall records.");

    private static readonly Action<ILogger, int, int, int, TimeSpan, Exception?> LogRetry =
        LoggerMessage.Define<int, int, int, TimeSpan>(LogLevel.Warning,
            new EventId(2, nameof(LogRetry)),
            "LlmCall batch persistence attempt {Attempt}/{MaxAttempts} failed for {Count} records; retrying in {Delay}.");

    private static readonly Action<ILogger, int, int, Exception?> LogExhausted =
        LoggerMessage.Define<int, int>(LogLevel.Critical,
            new EventId(3, nameof(LogExhausted)),
            "LlmCall batch persistence exhausted {MaxAttempts} attempts for {Count} records; rethrowing to crash host.");
}
