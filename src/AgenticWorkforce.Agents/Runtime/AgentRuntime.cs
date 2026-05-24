using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Agents.AI;
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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var agent = await factory.CreateAsync(entry, project, projectAgent, execContext, cts.Token).ConfigureAwait(false);
        var start = clock.GetTimestamp();

        logger.LogInformation(
            "Executing agent {AgentName} v{AgentVersion} for project {ProjectId} task {TaskId}",
            entry.AgentName, entry.AgentVersion, request.ProjectId, request.TaskId);

        try
        {
            var session = await agent.CreateSessionAsync(cts.Token).ConfigureAwait(false);
            var input = FormatObjective(request.Objective, request.Input);
            var response = await agent.RunAsync(input, session, cancellationToken: cts.Token).ConfigureAwait(false);

            var elapsed = clock.GetElapsedTime(start);
            // Per-iteration token/cost figures are written to LlmCalls by CostTrackingChatClient
            // and aggregated by CostQueryService; the per-execution rollup happens in Phase 7
            // (which also reads the per-task LlmCall sum). Until then, the response carries
            // the aggregate text only.
            return new AgentExecutionResult(
                Success: true,
                Output: response.Text ?? string.Empty,
                Error: null,
                InputTokens: 0,
                OutputTokens: 0,
                CostUsd: 0,
                DurationSeconds: elapsed.TotalSeconds,
                ToolCallCount: 0);
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
            logger.LogError(ex, "Agent {AgentName} execution failed for project {ProjectId}", entry.AgentName, request.ProjectId);
            throw new AgentExecutionException(entry.AgentName, ex.Message);
        }
    }

    private static string FormatObjective(string objective, string? input)
        => string.IsNullOrWhiteSpace(input) ? objective : $"{objective}\n\n## Input\n\n{input}";
}
