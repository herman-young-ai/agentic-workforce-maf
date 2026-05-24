using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Software;

/// <summary><c>software.code.analyze</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class CodeAnalysisTool
{
    public const string ToolName = "software.code.analyze";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        AnalyzeAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Run code-quality analysis (complexity, duplication, coupling) over a workspace path. Returns structured findings."
        });

    private static Task<string> AnalyzeAsync(
        [Description("Workspace-relative path to analyse.")] string targetPath,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
