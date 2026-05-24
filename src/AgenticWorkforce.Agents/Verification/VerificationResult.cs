namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Which tier of <see cref="VerificationPipeline"/> rejected an agent output.
/// </summary>
public enum FailureTier
{
    /// <summary>Tier 1 — output failed schema or structural validation.</summary>
    Tier1Structural,

    /// <summary>Tier 2 — output failed a business-rule check.</summary>
    Tier2Rules,

    /// <summary>Tier 3 — the system.verifier agent rejected the output.</summary>
    Tier3Agent
}

/// <summary>
/// Verdict from the verification pipeline. <see cref="Passed"/> = true means the
/// output is accepted; otherwise <see cref="FailedAt"/> identifies the tier that
/// rejected it and <see cref="Reason"/> / <see cref="Feedback"/> carry the
/// rationale (Reason is required on failures; Feedback is the actionable
/// remediation hint).
/// </summary>
public sealed record VerificationResult(
    bool Passed,
    FailureTier? FailedAt,
    string? Reason,
    string? Feedback)
{
    public static readonly VerificationResult Pass = new(true, null, null, null);

    public static VerificationResult Fail(FailureTier tier, string reason, string? feedback = null)
        => new(false, tier, reason, feedback);
}
