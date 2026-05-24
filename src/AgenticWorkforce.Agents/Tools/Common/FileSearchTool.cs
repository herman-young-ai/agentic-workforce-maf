using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary><c>file.search</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class FileSearchTool
{
    public const string ToolName = "file.search";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        SearchAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Search the workspace for files whose contents match a regular expression. Returns paths + matching line ranges."
        });

    private static Task<string> SearchAsync(
        [Description("Regular expression pattern to search for.")] string pattern,
        [Description("Optional path glob to scope the search (default: **/*).")] string? pathGlob = null,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
