namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Per-tool registration metadata.
/// </summary>
/// <param name="Name">Manifest name, e.g. "project.get_pcd" or "web.search".</param>
/// <param name="McpServer">Optional MCP server id (null for AIFunction-based tools).</param>
/// <param name="RequiresApproval">Phase 8 will gate execution via IWorkflowEngine; Phase 6 throws if any resolved tool sets this.</param>
/// <param name="Domain">Defaults to Sandbox (Principle 14: Secure by Default).</param>
internal sealed record ToolBinding(
    string Name,
    string? McpServer = null,
    bool RequiresApproval = false,
    ExecutionDomain Domain = ExecutionDomain.Sandbox);
