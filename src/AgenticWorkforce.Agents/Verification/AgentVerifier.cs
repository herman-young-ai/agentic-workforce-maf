using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Tier 3 — adversarial review by the <c>system.verifier</c> agent. Invoked
/// via the real <see cref="IAgentRuntime"/> contract (no shortcuts), so the
/// review call goes through every middleware in the pipeline (budget, cost
/// tracking, audit) just like a normal agent run.
///
/// <para><b>Recursion safety</b></para>
/// The <see cref="VerificationPipeline"/> guards against verifying the
/// verifier's own output (see <c>VerificationPipeline.SystemVerifierAgentName</c>).
/// </summary>
internal sealed class AgentVerifier(IAgentRuntime runtime, IOptions<AgentRuntimeOptions> options)
{
    /// <summary>Public so <see cref="VerificationPipeline"/> can compare against it.</summary>
    public const string AgentName = "system.verifier";

    private readonly int _previewChars = options.Value.VerificationPreviewChars;

    public async Task<VerificationResult> VerifyAsync(
        AgenticTask task, string output, AgentCatalog agent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(agent);

        var truncated = output.Length > _previewChars
            ? output[.._previewChars] + "…[truncated]"
            : output;

        var objective = $$"""
            Verify this agent output meets quality requirements.

            Task objective: {{task.Objective}}
            Agent: {{agent.AgentName}}
            Output to verify:
            {{truncated}}

            Respond with JSON: { "passed": true|false, "reason": "...", "feedback": "..." }
            """;

        var request = new AgentExecutionRequest(
            ProjectId: task.ProjectId,
            TaskId:    task.Id,
            AgentName: AgentName,
            Objective: objective);

        var result = await runtime.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return VerificationResult.Fail(FailureTier.Tier3Agent,
                $"system.verifier did not return a usable response: {result.Error ?? "no output"}",
                feedback: "Re-run verification after addressing the agent runtime issue.");
        }

        return ParseVerifierResponse(result.Output);
    }

    private static VerificationResult ParseVerifierResponse(string raw)
    {
        VerifierVerdict? verdict;
        try
        {
            verdict = JsonSerializer.Deserialize<VerifierVerdict>(raw, AgentJsonShapes.Options);
        }
        catch (JsonException ex)
        {
            return VerificationResult.Fail(FailureTier.Tier3Agent,
                $"system.verifier returned non-JSON output: {ex.Message}");
        }

        if (verdict is null)
        {
            return VerificationResult.Fail(FailureTier.Tier3Agent,
                "system.verifier returned a JSON null.");
        }

        return verdict.Passed
            ? VerificationResult.Pass
            : VerificationResult.Fail(FailureTier.Tier3Agent,
                string.IsNullOrWhiteSpace(verdict.Reason) ? "system.verifier rejected the output." : verdict.Reason,
                feedback: verdict.Feedback);
    }

    private sealed record VerifierVerdict(bool Passed, string? Reason, string? Feedback);
}
