using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary><c>file.write</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class FileWriteTool
{
    public const string ToolName = "file.write";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        WriteAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Write text content to a workspace file. Creates intermediate directories. Overwrites existing files."
        });

    private static Task<string> WriteAsync(
        [Description("Workspace-relative path to write.")] string path,
        [Description("Text content to write.")] string content,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
