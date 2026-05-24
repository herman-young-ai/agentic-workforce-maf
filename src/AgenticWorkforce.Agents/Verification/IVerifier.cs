using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Three-tier verifier for agent output. Implementations short-circuit at the
/// first tier that rejects, so callers can switch on
/// <see cref="VerificationResult.FailedAt"/> to decide whether the failure is
/// worth a retry (Tier 1/2 are deterministic — no point re-asking the agent
/// for the same output; Tier 3 may pass on retry once context is enriched).
/// </summary>
public interface IVerifier
{
    Task<VerificationResult> VerifyAsync(
        AgenticTask task,
        string output,
        AgentCatalog agent,
        CancellationToken cancellationToken = default);
}
