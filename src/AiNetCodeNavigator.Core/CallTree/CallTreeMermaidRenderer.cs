#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AiNetCodeNavigator.Core.CallTree;

/// <summary>
/// Rendert einen Aufrufgraphen als Mermaid-Flowchart-Diagramm.
/// </summary>
public static class CallTreeMermaidRenderer
{
    public static string RenderMermaid(CallGraphPayload graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");

        foreach (var node in graph.Nodes)
        {
            var label = EscapeLabel(FormatNode(node));
            sb.AppendLine($"    {node.NodeId}[\"{label}\"]");
        }

        foreach (var edge in graph.Edges)
        {
            sb.AppendLine($"    {edge.FromNodeId} --> {edge.ToNodeId}");
        }

        if (graph.HiddenEdgeCount > 0 && !string.IsNullOrEmpty(graph.RootNodeId))
        {
            sb.AppendLine($"    overflow[\"... und {graph.HiddenEdgeCount} weitere\"]");
            sb.AppendLine($"    {graph.RootNodeId} --> overflow");
        }

        var withHandoff = graph.Nodes.Where(n => !string.IsNullOrEmpty(n.HandoffId)).ToList();
        if (withHandoff.Count > 0)
        {
            sb.AppendLine();
            foreach (var node in withHandoff)
            {
                sb.AppendLine($"    %% [{node.NodeId}] handoffId: {node.HandoffId}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatNode(CallGraphNode node) =>
        string.IsNullOrWhiteSpace(node.DisplayLine)
            ? node.Name
            : $"{node.Name} — {node.DisplayLine}";

    private static string EscapeLabel(string label) =>
        label.Replace("\"", "'");
}
