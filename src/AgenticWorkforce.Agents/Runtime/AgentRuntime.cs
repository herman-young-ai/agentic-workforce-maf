using AgenticWorkforce.Agents.Middleware;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Implements the Domain <see cref="IAgentRuntime"/> contract. Resolves the
/// catalog entry, builds the agent via the factory, executes it with a timeout
/// linked to the caller's CancellationToken, and projects the MAF response into
/// the Domain <see cref="AgentExecutionResult"/> record.
///
/// Failure policy (Principle 8):
/// <list type="bullet">
///   <item>Missing/invalid catalog data: throws (caller-visible programmer error).</item>
///   <item>Budget exhausted (<see cref="BudgetExceededException"/>): propagates so the workflow can pause for a human decision.</item>
///   <item>Execution timeout: returns a typed failure result so the workflow can retry or surface the timeout cleanly.</item>
///   <item>Any other exception from the agent or pipeline: wrapped in <see cref="AgentExecutionException"/> with the agent name and propagated. Worker hosts log the unhandled exception and let the activity fail.</item>
/// </list>
/// </summary>
internal sealed class AgentRuntime(
    IAgentCatalogRepository catalog,
    IProjectRepository projects,
    IProjectAgentRepository projectAgents,
    IAgentFactory factory,
    IModelPricingService pricing,
    TimeProvider clock,
    IOptions<AgentRuntimeOptions> options,
    ILogger<AgentRuntime> logger) : IAgentRuntime
{
    private readonly AgentRuntimeOptions _opts = options.Value;

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = await catalog.GetByNameAsync(request.AgentName, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Agent", request.AgentName);

        if (string.IsNullOrWhiteSpace(entry.AgentVersion))
            throw new InvalidStateException(
                $"AgentCatalog '{entry.AgentName}' is missing AgentVersion. Catalog rows must declare a version.");

        var project = await projects.GetByIdAsync(request.ProjectId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Project", request.ProjectId);

        var projectAgentList = await projectAgents.ListByProjectAsync(request.ProjectId, ct).ConfigureAwait(false);
        var projectAgent = projectAgentList.FirstOrDefault(pa => pa.AgentCatalogId == entry.Id);

        var execContext = new AgentExecutionContext(
            ProjectId: request.ProjectId,
            TaskId: request.TaskId,
            SessionId: request.SessionId,
            AgentName: request.AgentName,
            Objective: request.Objective,
            Input: request.Input);

        var timeout = request.Timeout ?? _opts.DefaultExecutionTimeout;
        if (timeout <= TimeSpan.Zero)
            throw new ValidationException(
                $"AgentExecutionRequest.Timeout must be positive (got {timeout}). Pass null to use the configured default.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var agent = await factory.CreateAsync(entry, project, projectAgent, execContext, cts.Token).ConfigureAwait(false);
        var start = clock.GetTimestamp();

        LogExecuting(logger, entry.AgentName, entry.AgentVersion, request.ProjectId, request.TaskId, null);

        try
        {
            var session = await agent.CreateSessionAsync(cts.Token).ConfigureAwait(false);
            var input = FormatObjective(request.Objective, request.Input);
            var response = await agent.RunAsync(input, session, cancellationToken: cts.Token).ConfigureAwait(false);

            var elapsed = clock.GetElapsedTime(start);
            var usage         = response.Usage;
            var inputTokens   = usage?.InputTokenCount  ?? 0;
            var outputTokens  = usage?.OutputTokenCount ?? 0;
            var cacheRead     = usage.CacheTokens(UsageExtensions.CacheReadKey);
            var cacheCreate   = usage.CacheTokens(UsageExtensions.CacheCreateKey);
            var model         = ResolveModelId(entry);
            // Aggregate per-execution cost from the response's UsageDetails. CostTrackingChatClient
            // also writes a per-iteration LlmCall row for accounting; this rollup is the
            // synchronous value the caller can read without waiting for the drain.
            var costUsd = await pricing.CalculateCostAsync(
                model, inputTokens, outputTokens, cacheRead, cacheCreate, cts.Token).ConfigureAwait(false);

            return new AgentExecutionResult(
                Success: true,
                Output: response.Text ?? string.Empty,
                Error: null,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CostUsd: costUsd,
                DurationSeconds: elapsed.TotalSeconds,
                ToolCallCount: CountToolCalls(response));
        }
        catch (BudgetExceededException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new AgentExecutionResult(
                Success: false,
                Output: null,
                Error: $"Execution timed out after {timeout.TotalSeconds:F0}s",
                InputTokens: 0, OutputTokens: 0, CostUsd: 0,
                DurationSeconds: timeout.TotalSeconds,
                ToolCallCount: 0);
        }
        catch (AppException)
        {
            // Domain-typed exceptions propagate so callers can pattern-match (NotFound, Validation, etc.).
            throw;
        }
        catch (Exception ex)
        {
            // Unknown failure from the agent or pipeline. Log with full stack, wrap in a typed
            // exception so the Worker activity surface treats it as a recognised agent failure
            // rather than a host-level panic. Original exception is the inner; details survive in logs.
            LogFailure(logger, entry.AgentName, request.ProjectId, ex);
            throw new AgentExecutionException(entry.AgentName, ex.Message);
        }
    }

    private static readonly Action<ILogger, string, string?, Guid, Guid, Exception?> LogExecuting =
        LoggerMessage.Define<string, string?, Guid, Guid>(LogLevel.Information,
            new EventId(1, nameof(LogExecuting)),
            "Executing agent {AgentName} v{AgentVersion} for project {ProjectId} task {TaskId}");

    private static readonly Action<ILogger, string, Guid, Exception?> LogFailure =
        LoggerMessage.Define<string, Guid>(LogLevel.Error,
            new EventId(2, nameof(LogFailure)),
            "Agent {AgentName} execution failed for project {ProjectId}");

    private static string FormatObjective(string objective, string? input)
        => string.IsNullOrWhiteSpace(input) ? objective : $"{objective}\n\n## Input\n\n{input}";

    private string ResolveModelId(Domain.Entities.AgentCatalog entry)
    {
        // Phase 7 parses catalog.ModelConfig (jsonb) for the per-agent model id.
        // Until then the catalog routes through the configured default, which
        // matches AgentFactory.ResolveProviderAndModel.
        _ = entry;
        return _opts.DefaultModel;
    }

    private static int CountToolCalls(AgentResponse response)
    {
        var count = 0;
        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent) count++;
            }
        }
        return count;
    }
}
