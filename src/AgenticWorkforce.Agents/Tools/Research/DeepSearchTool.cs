using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Research;

/// <summary><c>research.web.search</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class DeepSearchTool
{
    public const string ToolName = "research.web.search";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        SearchAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Deep web search optimised for research workloads. Returns ranked hits with source-quality scores."
        });

    private static Task<string> SearchAsync(
        [Description("Sub-query string from the research plan.")] string query,
        [Description("Maximum hits to return.")] int maxResults = 10,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
