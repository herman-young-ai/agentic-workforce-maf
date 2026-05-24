using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Software;

/// <summary><c>software.arch.map</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class ArchitectureMapTool
{
    public const string ToolName = "software.arch.map";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        MapAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Produce a module + dependency map of the codebase. Returns nodes (modules) and edges (depends-on relationships)."
        });

    private static Task<string> MapAsync(
        [Description("Workspace-relative path to map.")] string targetPath,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
