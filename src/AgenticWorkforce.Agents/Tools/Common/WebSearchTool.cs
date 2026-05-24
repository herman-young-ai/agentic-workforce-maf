using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary><c>web.search</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class WebSearchTool
{
    public const string ToolName = "web.search";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        SearchAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Search the web for relevant pages via the configured search provider."
        });

    private static Task<string> SearchAsync(
        [Description("Search query string.")] string query,
        [Description("Maximum number of hits to return (provider may impose its own cap).")] int maxResults = 10,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
