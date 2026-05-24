using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Software;

/// <summary><c>software.test.run</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class TestRunTool
{
    public const string ToolName = "software.test.run";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        RunAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Run the project's test suite inside the agent's sandbox container. Returns pass/fail counts and per-test failure messages."
        });

    private static Task<string> RunAsync(
        [Description("Workspace-relative path to the project or solution under test.")] string targetPath,
        [Description("Optional test filter expression understood by the underlying runner.")] string? filter = null,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
