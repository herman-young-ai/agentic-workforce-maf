using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Security;

/// <summary><c>security.secrets.scan</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class SecretScanTool
{
    public const string ToolName = "security.secrets.scan";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ScanAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Scan workspace files for committed secrets. Returns the file/line of each hit and the detector that matched."
        });

    private static Task<string> ScanAsync(
        [Description("Workspace-relative path to scan.")] string targetPath,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
