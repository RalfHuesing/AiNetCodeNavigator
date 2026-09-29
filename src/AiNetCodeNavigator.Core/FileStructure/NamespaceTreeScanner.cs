#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>
/// Scannt und strukturiert deklarierte Namespaces und Typen einer Solution hierarchisch.
/// </summary>
public static class NamespaceTreeScanner
{
    public static async Task<NamespaceTreePayload> ScanSolutionAsync(
        Solution solution,
        string? projectName = null,
        CancellationToken ct = default)
    {
        var solutionName = Path.GetFileName(solution.FilePath) ?? "Solution";
        var projects = string.IsNullOrWhiteSpace(projectName)
            ? solution.Projects.ToList()
            : solution.Projects.Where(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase)).ToList();

        var nsTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalTypes = 0;

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            CollectNamespacesAndTypes(compilation.GlobalNamespace, nsTypeCounts, ref totalTypes);
        }

        var rootNodes = BuildTree(nsTypeCounts);
        var formatted = FormatTree(solutionName, projectName, rootNodes, nsTypeCounts.Count, totalTypes);

        return new NamespaceTreePayload(
            SolutionName: solutionName,
            ProjectName: projectName,
            RootNamespaces: rootNodes,
            TotalNamespaces: nsTypeCounts.Count,
            TotalTypes: totalTypes,
            FormattedText: formatted);
    }

    private static void CollectNamespacesAndTypes(
        INamespaceSymbol ns,
        Dictionary<string, int> nsTypeCounts,
        ref int totalTypes)
    {
        var typesInNs = ns.GetTypeMembers().Count(t => t.Locations.Any(l => l.IsInSource));
        if (typesInNs > 0 && !ns.IsGlobalNamespace)
        {
            var fullName = ns.ToDisplayString();
            nsTypeCounts[fullName] = nsTypeCounts.GetValueOrDefault(fullName) + typesInNs;
            totalTypes += typesInNs;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectNamespacesAndTypes(childNs, nsTypeCounts, ref totalTypes);
        }
    }

    private static List<NamespaceNode> BuildTree(Dictionary<string, int> nsTypeCounts)
    {
        var roots = new List<NamespaceNode>();
        var nodeMap = new Dictionary<string, NamespaceNode>(StringComparer.Ordinal);

        foreach (var (fullName, count) in nsTypeCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var parts = fullName.Split('.');
            var currentPath = "";
            NamespaceNode? parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = i == 0 ? part : $"{currentPath}.{part}";

                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new NamespaceNode(part, currentPath, i == parts.Length - 1 ? count : 0);
                    nodeMap[currentPath] = node;

                    if (parent is null)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }
                else if (i == parts.Length - 1)
                {
                    node.TypeCount = count;
                }

                parent = node;
            }
        }

        return roots;
    }

    private static string FormatTree(
        string solutionName,
        string? projectName,
        List<NamespaceNode> rootNodes,
        int totalNamespaces,
        int totalTypes)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrEmpty(projectName)
            ? $"# Namespace Tree: {solutionName}"
            : $"# Namespace Tree: {projectName} ({solutionName})";

        sb.AppendLine(title);
        sb.AppendLine($"> {totalNamespaces} Namespaces | {totalTypes} Typen");
        sb.AppendLine();

        foreach (var root in rootNodes)
        {
            AppendNode(sb, root, 0);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendNode(StringBuilder sb, NamespaceNode node, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var typeInfo = node.TypeCount > 0 ? $" ({node.TypeCount} Typen)" : "";
        sb.AppendLine($"{indentStr}- {node.Name}{typeInfo}");

        foreach (var child in node.Children.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            AppendNode(sb, child, indent + 1);
        }
    }
}
