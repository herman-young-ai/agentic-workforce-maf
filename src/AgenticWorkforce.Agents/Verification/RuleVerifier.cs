using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Tier 2 — deterministic business-rule checks that aren't expressible in JSON
/// Schema. Phase 7e enforces three rules; later phases will extend the set
/// with per-category checks (e.g. security findings reference real CVE IDs).
///
/// <list type="bullet">
///   <item>Non-empty output (whitespace-only is rejected).</item>
///   <item>Output length below 200 KB (sanity bound — Principle 19).</item>
///   <item>Task is not already in a terminal status when verification runs.</item>
/// </list>
/// </summary>
internal sealed class RuleVerifier
{
    private const int MaxOutputLength = 200_000;

    public VerificationResult Verify(AgenticTask task, string output)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (string.IsNullOrWhiteSpace(output))
        {
            return VerificationResult.Fail(FailureTier.Tier2Rules,
                "Agent output is empty.",
                feedback: "Produce a non-empty response that addresses the task objective.");
        }

        if (output.Length > MaxOutputLength)
        {
            return VerificationResult.Fail(FailureTier.Tier2Rules,
                $"Agent output is {output.Length} characters; max is {MaxOutputLength}.",
                feedback: $"Trim or summarise the output to fit within {MaxOutputLength} characters. Promote bulky detail to an Artifact instead.");
        }

        // Verification only runs against a task that just completed; if the task is already
        // in a terminal cancelled/skipped state, something upstream is out of order.
        if (task.Status is Domain.Enums.TaskStatus.Cancelled or Domain.Enums.TaskStatus.Skipped)
        {
            return VerificationResult.Fail(FailureTier.Tier2Rules,
                $"Task {task.Id} is already in terminal status {task.Status}; verification should not have run.");
        }

        return VerificationResult.Pass;
    }
}
