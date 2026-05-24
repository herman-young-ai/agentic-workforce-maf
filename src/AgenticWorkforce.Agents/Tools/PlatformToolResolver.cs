using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools;

/// <summary>
/// Combines every <see cref="IPlatformToolFactory"/> registered with DI
/// into a per-execution resolver. For each binding in the manifest whose
/// name matches a known factory, produces an <see cref="AITool"/> with
/// <c>projectId</c> captured. Bindings whose names are unknown are skipped —
/// the sandbox / MCP path (7d / 7e) handles those.
/// </summary>
internal sealed class PlatformToolResolver(
    IServiceProvider services,
    IEnumerable<IPlatformToolFactory> factories) : IPlatformToolResolver
{
    private readonly IServiceProvider _services = services;

    private readonly IReadOnlyDictionary<string, IPlatformToolFactory> _byName =
        factories.ToDictionary(f => f.ToolName, StringComparer.Ordinal);

    public IList<AITool> Resolve(IReadOnlyList<AgentToolBindingShape> manifest, Guid projectId)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var tools = new List<AITool>();
        foreach (var binding in manifest)
        {
            if (binding.RequiresApproval)
                throw new InvalidStateException(
                    $"Tool '{binding.Name}' requires human approval. Approval-gated tools land in Phase 8 alongside IWorkflowEngine.SubmitHumanInputAsync.");

            if (_byName.TryGetValue(binding.Name, out var factory))
                tools.Add(factory.Create(_services, projectId));
        }
        return tools;
    }
}
