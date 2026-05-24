using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgenticWorkforce.Agents.Tools.Mcp;

/// <summary>
/// Phase 7e stub. Real MCP client wiring (connect + capability discovery +
/// tool wrap) lands when the container sandbox is provisioned. Until then,
/// resolving any MCP binding throws <see cref="InvalidStateException"/> so
/// the failure is loud at agent construction time, not silent at first
/// invocation.
/// </summary>
internal sealed class McpToolResolver(ILogger<McpToolResolver> logger) : IMcpToolResolver
{
    public AITool Resolve(AgentToolBindingShape binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        LogMcpUnavailable(logger, binding.Name, binding.McpServer ?? "<unset>", null);
        throw new InvalidStateException(
            $"MCP tool '{binding.Name}' requires server '{binding.McpServer ?? "<unset>"}' which is not yet configured.");
    }

    private static readonly Action<ILogger, string, string, Exception?> LogMcpUnavailable =
        LoggerMessage.Define<string, string>(LogLevel.Warning,
            new EventId(1, nameof(LogMcpUnavailable)),
            "MCP tool {ToolName} from server {McpServer} not yet available.");
}
