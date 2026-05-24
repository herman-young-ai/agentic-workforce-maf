namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// The service-account identity that owns agent-initiated writes (proposed
/// tasks, PCD additions, learning extractions). Agents do not have a per-execution
/// triggering user in Phase 7 (Phase 8 will thread the workflow's triggering
/// actor through <c>AgentExecutionContext</c>); until then every agent-driven
/// mutation is attributed to this single platform service account so the audit
/// trail still names a concrete actor (Principle 11 — segregation of duties
/// remains enforceable because the platform actor cannot also approve).
/// </summary>
public interface IPlatformActor
{
    /// <summary>The User row id under which agent-initiated writes are recorded.</summary>
    Guid UserId { get; }

    /// <summary>The email associated with the service-account user.</summary>
    string Email { get; }
}
