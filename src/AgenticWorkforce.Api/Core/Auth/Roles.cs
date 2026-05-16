namespace AgenticWorkforce.Api.Core.Auth;

/// <summary>
/// App role values from Entra ID app registration.
/// Two-dimensional model: platform roles + project roles (ADR-007).
/// Hierarchical: higher roles include all lower role permissions.
/// </summary>
public static class Roles
{
    // -- Human identities (platform-level) --
    public const string PlatformAdmin = "AgenticWorkforce.PlatformAdmin";
    public const string Owner         = "AgenticWorkforce.Owner";
    public const string Reviewer      = "AgenticWorkforce.Reviewer";
    public const string Operator      = "AgenticWorkforce.Operator";
    public const string Viewer        = "AgenticWorkforce.Viewer";

    // -- Non-human / AI agent identities --
    public const string Agent         = "AgenticWorkforce.Agent";
    public const string AgentReadOnly = "AgenticWorkforce.AgentReadOnly";
}

/// <summary>
/// Authorization policy names — used with [Authorize(Policy = Policies.XxxXxx)].
/// Policies are hierarchical: higher roles always include lower ones.
/// </summary>
public static class Policies
{
    // Human policies (hierarchical)
    public const string RequirePlatformAdmin = nameof(RequirePlatformAdmin);
    public const string RequireOwner         = nameof(RequireOwner);
    public const string RequireReviewer      = nameof(RequireReviewer);
    public const string RequireOperator      = nameof(RequireOperator);
    public const string RequireViewer        = nameof(RequireViewer);

    // Agent policies
    public const string RequireAgent         = nameof(RequireAgent);
    public const string RequireAgentReadOnly = nameof(RequireAgentReadOnly);

    // Mixed — human or agent (for endpoints called by both)
    public const string RequireAuthenticatedAny = nameof(RequireAuthenticatedAny);
}
