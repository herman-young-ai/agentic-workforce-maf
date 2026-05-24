using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary>
/// <c>file.read</c> — Phase 7 sandbox stub. Throws
/// <see cref="SandboxUnavailableException"/> until ACA Dynamic Sessions
/// integration lands in Phase 11.
/// </summary>
internal static class FileReadTool
{
    public const string ToolName = "file.read";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ReadAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Read the contents of a single workspace file. Returns the file body as text."
        });

    private static Task<string> ReadAsync(
        [Description("Workspace-relative path to read.")] string path,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
