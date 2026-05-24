using AgenticWorkforce.Domain.Agents;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Mcp;

/// <summary>
/// Resolves an MCP-backed tool binding (one whose
/// <see cref="AgentToolBindingShape.McpServer"/> is set) to a concrete
/// <see cref="AITool"/>. Real implementations connect to the MCP server,
/// negotiate tool capabilities, and wrap the call; Phase 7 ships a stub that
/// throws.
/// </summary>
internal interface IMcpToolResolver
{
    AITool Resolve(AgentToolBindingShape binding);
}
