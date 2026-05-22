using System.Text.Json;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Domain.Services;

/// <summary>
/// Pure validator for workflow graph JSON. Each rule reports a distinct error
/// rather than fail-fast so authors see every problem in one pass.
///
/// Expected shapes (lenient — only the named fields are required):
///   nodes: [{ "id": "n1", "type": "Start" | "End" | "Decision" | ... }, ...]
///   edges: [{ "from": "n1", "to": "n2", "label": "optional condition" }, ...]
/// </summary>
public sealed class WorkflowValidator : IWorkflowValidator
{
    public WorkflowValidationResult Validate(string nodesJson, string edgesJson)
    {
        var errors = new List<WorkflowValidationError>();

        List<NodeRef>? nodes;
        List<EdgeRef>? edges;
        try
        {
            nodes = ParseNodes(nodesJson);
            edges = ParseEdges(edgesJson);
        }
        catch (JsonException ex)
        {
            errors.Add(new WorkflowValidationError(
                WorkflowValidationCause.MalformedJson,
                $"Could not parse nodes/edges JSON: {ex.Message}"));
            return new WorkflowValidationResult(false, errors);
        }

        var startCount = nodes.Count(n => string.Equals(n.Type, "Start", StringComparison.OrdinalIgnoreCase));
        if (startCount != 1)
            errors.Add(new WorkflowValidationError(
                WorkflowValidationCause.StartNodeCount,
                $"Workflow must have exactly one Start node (found {startCount})."));

        if (!nodes.Any(n => string.Equals(n.Type, "End", StringComparison.OrdinalIgnoreCase)))
            errors.Add(new WorkflowValidationError(
                WorkflowValidationCause.NoEndNode,
                "Workflow must have at least one End node."));

        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To))
            {
                errors.Add(new WorkflowValidationError(
                    WorkflowValidationCause.DanglingEdge,
                    $"Edge references unknown node id: {edge.From} -> {edge.To}."));
            }
        }

        // Adjacency for reachability + cycle detection. Only build it once
        // dangling edges are accounted for so we don't crash on missing keys.
        var adj = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var e in edges)
            if (adj.ContainsKey(e.From) && adj.ContainsKey(e.To))
                adj[e.From].Add(e.To);

        if (startCount == 1)
        {
            var startId = nodes.First(n => string.Equals(n.Type, "Start", StringComparison.OrdinalIgnoreCase)).Id;
            var reachable = ReachableFrom(startId, adj);
            var orphans = nodeIds.Except(reachable).ToList();
            if (orphans.Count > 0)
                errors.Add(new WorkflowValidationError(
                    WorkflowValidationCause.OrphanNode,
                    $"Nodes unreachable from Start: {string.Join(", ", orphans)}."));
        }

        foreach (var node in nodes.Where(n =>
            string.Equals(n.Type, "Decision", StringComparison.OrdinalIgnoreCase)
         || string.Equals(n.Type, "AiDecision", StringComparison.OrdinalIgnoreCase)
         || string.Equals(n.Type, "HumanDecision", StringComparison.OrdinalIgnoreCase)))
        {
            var outgoing = edges.Where(e => e.From == node.Id).ToList();
            var unlabeled = outgoing.Where(e => string.IsNullOrWhiteSpace(e.Label)).ToList();
            if (unlabeled.Count > 0)
                errors.Add(new WorkflowValidationError(
                    WorkflowValidationCause.DecisionEdgeMissingLabel,
                    $"Decision node '{node.Id}' has unlabeled outgoing edge(s); each branch must have a condition label."));
        }

        if (HasCycle(adj))
            errors.Add(new WorkflowValidationError(
                WorkflowValidationCause.Cycle,
                "Workflow graph contains a cycle; the graph must be a DAG."));

        return new WorkflowValidationResult(errors.Count == 0, errors);
    }

    private static List<NodeRef> ParseNodes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<NodeRef>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            // TryGetProperty rather than GetProperty so a missing required
            // field surfaces as a MalformedJson validation error (caught
            // upstream) instead of an uncaught KeyNotFoundException that
            // would turn into a 500.
            if (!el.TryGetProperty("id", out var idEl) || idEl.GetString() is not { } id)
                throw new JsonException("Node missing required 'id' string field.");
            var type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            list.Add(new NodeRef(id, type));
        }
        return list;
    }

    private static List<EdgeRef> ParseEdges(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<EdgeRef>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("from", out var fromEl) || fromEl.GetString() is not { } from)
                throw new JsonException("Edge missing required 'from' string field.");
            if (!el.TryGetProperty("to", out var toEl) || toEl.GetString() is not { } to)
                throw new JsonException("Edge missing required 'to' string field.");
            var label = el.TryGetProperty("label", out var l) ? l.GetString() : null;
            list.Add(new EdgeRef(from, to, label));
        }
        return list;
    }

    private static HashSet<string> ReachableFrom(string start, Dictionary<string, List<string>> adj)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!seen.Add(n)) continue;
            if (adj.TryGetValue(n, out var next))
                foreach (var to in next) stack.Push(to);
        }
        return seen;
    }

    private static bool HasCycle(Dictionary<string, List<string>> adj)
    {
        const int White = 0, Grey = 1, Black = 2;
        var colour = adj.Keys.ToDictionary(k => k, _ => White, StringComparer.Ordinal);

        bool Visit(string node)
        {
            colour[node] = Grey;
            foreach (var next in adj[node])
            {
                if (!colour.TryGetValue(next, out var c)) continue;
                if (c == Grey) return true;
                if (c == White && Visit(next)) return true;
            }
            colour[node] = Black;
            return false;
        }

        foreach (var n in adj.Keys)
            if (colour[n] == White && Visit(n)) return true;
        return false;
    }

    private sealed record NodeRef(string Id, string Type);
    private sealed record EdgeRef(string From, string To, string? Label);
}
