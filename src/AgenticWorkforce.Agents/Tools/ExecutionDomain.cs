namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Where a tool runs (Principle 22: Container-First). Default for any new
/// tool is <see cref="Sandbox"/> — Platform requires explicit registration
/// and the tool must implement <see cref="IPlatformTool"/>.
/// </summary>
internal enum ExecutionDomain
{
    /// <summary>In-process. Restricted to internal DB-reading tools.</summary>
    Platform,

    /// <summary>ACA Dynamic Sessions container. Default for everything else.</summary>
    Sandbox
}
