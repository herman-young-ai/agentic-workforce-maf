using System.Collections.Concurrent;
using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Tools;

internal sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, (ToolBinding Binding, AITool Tool)> _tools = new(StringComparer.Ordinal);

    public void Register(ToolBinding binding, AITool tool)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(tool);
        if (!_tools.TryAdd(binding.Name, (binding, tool)))
            throw new InvalidStateException($"Tool '{binding.Name}' is already registered.");
    }

    public IList<AITool> Resolve(IReadOnlyList<ToolBinding> manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var resolved = new List<AITool>(manifest.Count);
        foreach (var bind in manifest)
        {
            if (!_tools.TryGetValue(bind.Name, out var entry))
                throw new InvalidStateException($"Tool '{bind.Name}' is not registered in the ToolRegistry.");

            if (bind.RequiresApproval)
                throw new InvalidStateException(
                    $"Tool '{bind.Name}' requires human approval. Approval-required tools land in Phase 8 with IWorkflowEngine; no Phase 6 manifest may set RequiresApproval = true.");

            resolved.Add(entry.Tool);
        }
        return resolved;
    }
}
