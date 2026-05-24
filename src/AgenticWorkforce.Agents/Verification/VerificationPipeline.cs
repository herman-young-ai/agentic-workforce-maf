using AgenticWorkforce.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Three-tier verification: Schema (Tier 1) → Rules (Tier 2) → Agent (Tier 3).
/// Short-circuits at the first failing tier. Tier 3 runs only when the agent
/// under review declares <see cref="AgentCatalog.ProducesArtifact"/> AND is not
/// itself <see cref="AgentVerifier.AgentName"/> — the latter guard prevents
/// the pipeline from recursively verifying its own verifier.
/// </summary>
internal sealed class VerificationPipeline(
    SchemaVerifier schema,
    RuleVerifier rules,
    AgentVerifier agentVerifier,
    ILogger<VerificationPipeline> logger) : IVerifier
{
    public async Task<VerificationResult> VerifyAsync(
        AgenticTask task,
        string output,
        AgentCatalog agent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(agent);

        var schemaResult = schema.Verify(output, agent);
        if (!schemaResult.Passed)
        {
            LogFailed(logger, FailureTier.Tier1Structural, task.Id, agent.AgentName, schemaResult.Reason ?? string.Empty, null);
            return schemaResult;
        }

        var ruleResult = rules.Verify(task, output);
        if (!ruleResult.Passed)
        {
            LogFailed(logger, FailureTier.Tier2Rules, task.Id, agent.AgentName, ruleResult.Reason ?? string.Empty, null);
            return ruleResult;
        }

        // Recursion guard: verifying the verifier's own output would call ExecuteAsync
        // on system.verifier again, which would re-enter this pipeline. Skip Tier 3
        // for that single agent; its outputs are still subject to Tier 1 + Tier 2.
        if (agent.ProducesArtifact
            && !string.Equals(agent.AgentName, AgentVerifier.AgentName, StringComparison.Ordinal))
        {
            var agentResult = await agentVerifier.VerifyAsync(task, output, agent, cancellationToken).ConfigureAwait(false);
            if (!agentResult.Passed)
            {
                LogFailed(logger, FailureTier.Tier3Agent, task.Id, agent.AgentName, agentResult.Reason ?? string.Empty, null);
                return agentResult;
            }
        }

        return VerificationResult.Pass;
    }

    private static readonly Action<ILogger, FailureTier, Guid, string, string, Exception?> LogFailed =
        LoggerMessage.Define<FailureTier, Guid, string, string>(LogLevel.Warning,
            new EventId(1, nameof(LogFailed)),
            "Verification failed at {Tier} for task {TaskId} (agent {AgentName}): {Reason}");
}
