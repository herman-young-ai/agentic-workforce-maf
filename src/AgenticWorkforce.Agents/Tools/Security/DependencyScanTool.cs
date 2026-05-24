using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Security;

/// <summary><c>security.deps.scan</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class DependencyScanTool
{
    public const string ToolName = "security.deps.scan";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ScanAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Scan project dependencies for known CVEs. Returns findings keyed by package and version."
        });

    private static Task<string> ScanAsync(
        [Description("Workspace-relative path to a manifest (e.g. package.json, *.csproj, requirements.txt) or directory.")] string targetPath,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
