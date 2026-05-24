using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Research;

/// <summary><c>research.extract</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class ContentExtractTool
{
    public const string ToolName = "research.extract";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        ExtractAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Extract the main textual content from a single URL, stripped of chrome, ads, and navigation."
        });

    private static Task<string> ExtractAsync(
        [Description("Absolute URL to extract from.")] string url,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
