using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Research;

/// <summary><c>research.source.evaluate</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class SourceEvaluateTool
{
    public const string ToolName = "research.source.evaluate";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        EvaluateAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Score a source URL for trustworthiness (domain reputation, freshness, citation density). Returns a 0-1 score and rationale."
        });

    private static Task<string> EvaluateAsync(
        [Description("Source URL to evaluate.")] string url,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
