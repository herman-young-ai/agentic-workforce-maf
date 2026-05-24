using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Security;

/// <summary><c>security.code.scan</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class CodeScanTool
{
    public const string ToolName = "security.code.scan";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ScanAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Run a static-analysis scan against the target path. Returns structured findings (severity, file, line, category)."
        });

    private static Task<string> ScanAsync(
        [Description("Workspace-relative path to scan.")] string targetPath,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
