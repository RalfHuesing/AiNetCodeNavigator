#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AiNetCodeNavigator.Core.CallTree;

/// <summary>
/// Renders a call graph as an ASCII tree.
/// </summary>
public static class CallGraphTextRenderer
{
    public static string RenderAscii(CallGraphPayload graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Nodes.Count == 0) return "No calls found.";

        var nodes = graph.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        if (!nodes.TryGetValue(graph.RootNodeId, out var root))
        {
            root = graph.Nodes[0];
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[{root.NodeId}] {FormatNode(root)}");

        for (int i = 0; i < graph.Edges.Count; i++)
        {
            var edge = graph.Edges[i];
            if (!nodes.TryGetValue(edge.FromNodeId, out var from) || !nodes.TryGetValue(edge.ToNodeId, out var to))
            {
                continue;
            }

            var isLast = i == graph.Edges.Count - 1 && graph.HiddenEdgeCount == 0;
            var prefix = isLast ? "└── " : "├── ";

            var locations = edge.CallSites.Count == 0 ? string.Empty : " — " + string.Join(", ",
                edge.CallSites.Select(site => $"{site.FilePath}:{site.Line}:{site.Column} [{site.EvidenceKind}]"));

            sb.AppendLine($"{prefix}[{from.NodeId}] {from.Name} -> [{to.NodeId}] {to.Name}{locations}");
        }

        if (graph.HiddenEdgeCount > 0)
        {
            sb.AppendLine($"└── ... and {graph.HiddenEdgeCount} more calls");
        }

        if (graph.PendingNodeCount > 0)
        {
            sb.AppendLine($"└── ... {graph.PendingNodeCount} nodes not yet explored");
        }

        if (graph.UnresolvedCallSites is { Count: > 0 })
        {
            sb.AppendLine("Unresolved call sites:");
            foreach (var unresolved in graph.UnresolvedCallSites)
            {
                var candidates = unresolved.CandidateTargets.Count == 0
                    ? string.Empty
                    : $"; possible targets: {string.Join(", ", unresolved.CandidateTargets)}";
                sb.AppendLine($"- [{unresolved.EvidenceKind}] {unresolved.FilePath}:{unresolved.Line}:{unresolved.Column}{candidates}");
            }
        }

        AppendNodeHandoffs(sb, graph.Nodes);
        AppendMethodHints(sb, graph.MethodHints);

        return sb.ToString().TrimEnd();
    }

    private static string FormatNode(CallGraphNode node) =>
        string.IsNullOrWhiteSpace(node.DisplayLine)
            ? node.Name
            : $"{node.Name} — {node.DisplayLine}";

    private static void AppendNodeHandoffs(StringBuilder sb, IReadOnlyList<CallGraphNode> nodes)
    {
        var withHandoff = nodes.Where(n => !string.IsNullOrEmpty(n.HandoffId)).ToList();
        if (withHandoff.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("Handoffs:");
        foreach (var node in withHandoff)
        {
            var owner = string.IsNullOrWhiteSpace(node.OwnerTargetPath) ? string.Empty : $" — targetPath: {node.OwnerTargetPath}";
            sb.AppendLine($"- [{node.NodeId}] `{node.HandoffId}` ({node.Name}){owner}");
        }
    }

    private static void AppendMethodHints(StringBuilder sb, IReadOnlyList<CallGraphMethodHint>? hints)
    {
        if (hints is null || hints.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("Methods in type:");
        foreach (var hint in hints)
        {
            sb.AppendLine($"- {hint.Name} — {hint.DisplayLine}");
        }
    }
}
