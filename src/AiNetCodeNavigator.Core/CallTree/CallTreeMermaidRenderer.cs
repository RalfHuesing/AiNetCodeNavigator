#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AiNetCodeNavigator.Core.CallTree;

/// <summary>
/// Renders a call graph as a Mermaid flowchart.
/// </summary>
public static class CallTreeMermaidRenderer
{
    public static string RenderMermaid(CallGraphPayload graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");

        foreach (var node in graph.Nodes)
        {
            var label = EscapeLabel(FormatNode(node));
            sb.AppendLine($"    {node.NodeId}[\"{label}\"]");
        }

        foreach (var edge in graph.Edges)
        {
            var evidence = edge.CallSites.Select(site => site.EvidenceKind).Distinct(StringComparer.Ordinal).ToArray();
            var label = evidence.Length == 0 ? string.Empty : $"|{EscapeLabel(string.Join(", ", evidence))} ×{edge.CallSites.Count}|";
            sb.AppendLine($"    {edge.FromNodeId} -->{label} {edge.ToNodeId}");
            foreach (var site in edge.CallSites)
                sb.AppendLine($"    %% {EscapeComment($"{site.FilePath}:{site.Line}:{site.Column} [{site.EvidenceKind}]")}");
        }

        if (graph.UnresolvedCallSites is { Count: > 0 })
        {
            sb.AppendLine();
            foreach (var unresolved in graph.UnresolvedCallSites)
            {
                var candidates = unresolved.CandidateTargets.Count == 0
                    ? string.Empty
                    : $"; possible targets: {string.Join(", ", unresolved.CandidateTargets)}";
                sb.AppendLine($"    %% [{unresolved.EvidenceKind}] {unresolved.FilePath}:{unresolved.Line}:{unresolved.Column}{candidates}");
            }
        }

        if (graph.HiddenEdgeCount > 0 && !string.IsNullOrEmpty(graph.RootNodeId))
        {
            sb.AppendLine($"    overflow[\"... and {graph.HiddenEdgeCount} more\"]");
            sb.AppendLine($"    {graph.RootNodeId} --> overflow");
        }

        if (graph.PendingNodeCount > 0 && !string.IsNullOrEmpty(graph.RootNodeId))
        {
            sb.AppendLine($"    pending[\"... {graph.PendingNodeCount} nodes not explored\"]");
            sb.AppendLine($"    {graph.RootNodeId} --> pending");
        }

        var withHandoff = graph.Nodes.Where(n => !string.IsNullOrEmpty(n.HandoffId)).ToList();
        if (withHandoff.Count > 0)
        {
            sb.AppendLine();
            foreach (var node in withHandoff)
            {
                var owner = string.IsNullOrWhiteSpace(node.OwnerTargetPath) ? string.Empty : $"; targetPath: {node.OwnerTargetPath}";
                sb.AppendLine($"    %% [{node.NodeId}] handoffId: {node.HandoffId}{owner}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatNode(CallGraphNode node) =>
        string.IsNullOrWhiteSpace(node.DisplayLine)
            ? node.Name
            : $"{node.Name} — {node.DisplayLine}";

    private static string EscapeLabel(string label) =>
        label.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Replace("|", "/");

    private static string EscapeComment(string value) => value.Replace("\r", " ").Replace("\n", " ");
}
