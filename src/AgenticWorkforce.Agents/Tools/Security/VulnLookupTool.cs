using System.ComponentModel;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools.Security;

/// <summary><c>security.vuln.lookup</c> — Phase 7 sandbox stub; throws until Phase 11.</summary>
internal static class VulnLookupTool
{
    public const string ToolName = "security.vuln.lookup";

    public static AITool CreateBinding() => AIFunctionFactory.Create(
        LookupAsync,
        new AIFunctionFactoryOptions
        {
            Name = ToolName,
            Description = "Look up known vulnerabilities for a specific package + version using the configured CVE source."
        });

    private static Task<string> LookupAsync(
        [Description("Package name (e.g. lodash, Newtonsoft.Json).")] string packageName,
        [Description("Version string. Pass null to query the latest known.")] string? version = null,
        CancellationToken cancellationToken = default)
        => throw new SandboxUnavailableException(ToolName);
}
