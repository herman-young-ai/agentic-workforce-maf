using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Common;

/// <summary><c>web.fetch</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class WebFetchTool
{
    public const string ToolName = "web.fetch";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        FetchAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Fetch the rendered body of a single URL. Subject to the sandbox egress allowlist."
        });

    private static Task<string> FetchAsync(
        [Description("Absolute URL to fetch.")] string url,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
