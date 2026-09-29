#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AiNetCodeNavigator.Core.CallTree;

/// <summary>
/// Rendert einen Aufrufgraphen als ASCII-Baumdarstellung.
/// </summary>
public static class CallGraphTextRenderer
{
    public static string RenderAscii(CallGraphPayload graph)
    {
        if (graph.Nodes.Count == 0) return "Keine Aufrufe gefunden.";

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

            var site = edge.CallSites.FirstOrDefault();
            var location = site is null ? string.Empty : $" — {site.FilePath}:{site.Line}";
            var dispatch = string.IsNullOrWhiteSpace(edge.DispatchKind) ? string.Empty : $" [{edge.DispatchKind}]";

            sb.AppendLine($"{prefix}[{from.NodeId}] {from.Name} -> [{to.NodeId}] {to.Name}{location}{dispatch}");
        }

        if (graph.HiddenEdgeCount > 0)
        {
            sb.AppendLine($"└── ... und {graph.HiddenEdgeCount} weitere Aufrufe");
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
            sb.AppendLine($"- [{node.NodeId}] `{node.HandoffId}` ({node.Name})");
        }
    }

    private static void AppendMethodHints(StringBuilder sb, IReadOnlyList<CallGraphMethodHint>? hints)
    {
        if (hints is null || hints.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("Methoden im Typ:");
        foreach (var hint in hints)
        {
            sb.AppendLine($"- {hint.Name} — {hint.DisplayLine}");
        }
    }
}
