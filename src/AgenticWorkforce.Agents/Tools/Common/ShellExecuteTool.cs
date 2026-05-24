using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary><c>shell.execute</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class ShellExecuteTool
{
    public const string ToolName = "shell.execute";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ExecuteAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Execute a shell command inside the agent's sandbox container. Returns combined stdout + stderr."
        });

    private static Task<string> ExecuteAsync(
        [Description("Shell command to run.")] string command,
        [Description("Working directory (workspace-relative). Defaults to workspace root.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
