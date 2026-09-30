#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.FileStructure;

/// <summary>Scans declared source namespaces and types into a bounded hierarchy.</summary>
public static class NamespaceTreeScanner
{
    public const int MaxDepthCap = 32;
    public const int DefaultMaxResults = 50;
    public const int MaxResultsCap = 200;

    public static async Task<NamespaceTreePayload> ScanSolutionAsync(
        Solution solution,
        string? projectName = null,
        CancellationToken ct = default,
        NamespaceTreeScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ct.ThrowIfCancellationRequested();

        var requestedOptions = options ?? new NamespaceTreeScanOptions();
        var effectiveDepth = ClampBound(requestedOptions.MaxDepth, MaxDepthCap);
        var effectiveResults = ClampBound(requestedOptions.MaxResults, MaxResultsCap);
        var prefix = string.IsNullOrWhiteSpace(requestedOptions.NamespacePrefix) ? null : requestedOptions.NamespacePrefix.Trim().Trim('.');
        var kind = requestedOptions.Kind.Trim().ToLowerInvariant();
        var validKinds = new[] { "all", "class", "record", "struct", "interface", "enum", "delegate" };
        if (!validKinds.Contains(kind, StringComparer.Ordinal))
            return CreatePayload(Path.GetFileName(solution.FilePath) ?? "Solution", projectName, [], 0, 0, false, [],
                $"Unsupported namespace type kind '{requestedOptions.Kind}'.", requestedOptions, effectiveDepth, effectiveResults,
                effectiveDepth != requestedOptions.MaxDepth || effectiveResults != requestedOptions.MaxResults);
        var prefixDepth = prefix?.Count(character => character == '.') ?? 0;
        var scanDepth = Math.Min(MaxDepthCap, effectiveDepth + prefixDepth);
        var boundsWereClamped = effectiveDepth != requestedOptions.MaxDepth || effectiveResults != requestedOptions.MaxResults
            || prefix is not null && scanDepth < effectiveDepth + prefixDepth;
        var solutionName = Path.GetFileName(solution.FilePath) ?? "Solution";
        var requestedProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim();
        var namedProjects = requestedProjectName is null
            ? solution.Projects.ToList()
            : solution.Projects.Where(p => string.Equals(p.Name, requestedProjectName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (requestedProjectName is not null && namedProjects.Count == 0)
        {
            var error = $"Project '{requestedProjectName}' was not found.";
            return CreatePayload(solutionName, requestedProjectName, [], 0, 0, false, [], error,
                requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
        }

        var selectedProject = requestedProjectName is null ? null : namedProjects[0];
        if (selectedProject is not null && !string.Equals(selectedProject.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            var error = $"Project '{selectedProject.Name}' is not a C# project.";
            return CreatePayload(solutionName, selectedProject.Name, [], 0, 0, false, [], error,
                requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
        }

        var selectedProjectName = selectedProject?.Name;
        var projects = namedProjects
            .Where(project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
            .ToList();

        var nsTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalTypes = 0;
        var depthWasTruncated = false;

        try
        {
            foreach (var project in projects)
            {
                ct.ThrowIfCancellationRequested();
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation is null)
                {
                    var error = $"Project '{project.Name}' could not be compiled.";
                    return CreatePayload(solutionName, selectedProjectName, [], 0, 0, false, [], error,
                        requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
                }

                var sourceTrees = await GetProjectSourceTreesAsync(project, requestedOptions.IncludeGenerated, ct).ConfigureAwait(false);
                CollectNamespacesAndTypes(
                    compilation.GlobalNamespace,
                    sourceTrees,
                    [],
                    scanDepth,
                    nsTypeCounts,
                    ref totalTypes,
                    ref depthWasTruncated,
                    ct,
                    kind,
                    requestedOptions.IncludeTypes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = $"Namespace tree scan failed: {ex.Message}";
            return CreatePayload(solutionName, selectedProjectName, [], 0, 0, false, [], error,
                requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
        }

        var fullTree = BuildTree(nsTypeCounts);
        if (prefix is not null)
        {
            var prefixNode = FindNode(fullTree, prefix);
            if (prefixNode is null)
                return CreatePayload(solutionName, selectedProjectName, [], 0, 0, false, [], $"Namespace prefix '{prefix}' was not found.",
                    requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
            var scopedNode = PruneAtDepth(prefixNode, effectiveDepth, 0, out var prefixDepthTruncated);
            fullTree = [scopedNode];
            depthWasTruncated |= prefixDepthTruncated;
        }
        var totalNamespaces = CountNodes(fullTree);
        var rootNodes = TakeTree(fullTree, effectiveResults, out var shownNamespaces);
        var resultLimitWasReached = shownNamespaces < totalNamespaces;
        var truncatedBy = new List<string>(2);
        if (depthWasTruncated) truncatedBy.Add("maxDepth");
        if (resultLimitWasReached) truncatedBy.Add("maxResults");

        var nextAction = GetNextAction(truncatedBy);
        var formatted = FormatTree(solutionName, selectedProjectName, rootNodes, totalNamespaces, shownNamespaces, totalTypes,
            requestedOptions.IncludeGenerated, truncatedBy, nextAction);
        return CreatePayload(
            solutionName,
            selectedProjectName,
            rootNodes,
            totalNamespaces,
            totalTypes,
            truncatedBy.Count > 0,
            truncatedBy,
            null,
            requestedOptions,
            effectiveDepth,
            effectiveResults,
            boundsWereClamped,
            formatted,
            shownNamespaces);
    }

    private static int ClampBound(int requested, int cap) => requested < 1 ? 1 : Math.Min(requested, cap);

    private static async Task<HashSet<SyntaxTree>> GetProjectSourceTreesAsync(
        Project project,
        bool includeGenerated,
        CancellationToken ct)
    {
        var trees = new HashSet<SyntaxTree>();
        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false))
            {
                continue;
            }

            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (tree is not null) trees.Add(tree);
        }

        return trees;
    }

    private static void CollectNamespacesAndTypes(
        INamespaceSymbol ns,
        HashSet<SyntaxTree> sourceTrees,
        List<string> parentPath,
        int maxDepth,
        Dictionary<string, int> nsTypeCounts,
        ref int totalTypes,
        ref bool depthWasTruncated,
        CancellationToken ct,
        string kind,
        bool includeTypes)
    {
        ct.ThrowIfCancellationRequested();
        var path = ns.IsGlobalNamespace ? parentPath : [.. parentPath, ns.Name];

        if (!ns.IsGlobalNamespace)
        {
            var matchingTypes = ns.GetTypeMembers().Where(type => MatchesKind(type, kind) && type.Locations.Any(location =>
                location.IsInSource && location.SourceTree is not null && sourceTrees.Contains(location.SourceTree))).ToArray();
            if (matchingTypes.Length > 0)
            {
                var fullName = string.Join('.', path);
                nsTypeCounts[fullName] = nsTypeCounts.GetValueOrDefault(fullName) + (includeTypes ? matchingTypes.Length : 0);
                totalTypes += matchingTypes.Length;
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (!ns.IsGlobalNamespace && path.Count >= maxDepth)
            {
                var typesBelowDepth = CountProjectSourceTypesInHierarchy(childNs, sourceTrees, ct, kind);
                if (typesBelowDepth > 0)
                {
                    depthWasTruncated = true;
                    var visiblePrefix = string.Join('.', path);
                    nsTypeCounts.TryAdd(visiblePrefix, 0);
                    totalTypes += typesBelowDepth;
                }
                continue;
            }

            CollectNamespacesAndTypes(childNs, sourceTrees, path, maxDepth, nsTypeCounts, ref totalTypes, ref depthWasTruncated, ct, kind, includeTypes);
        }
    }

    private static int CountProjectSourceTypesInHierarchy(INamespaceSymbol ns, HashSet<SyntaxTree> sourceTrees, CancellationToken ct, string kind)
    {
        ct.ThrowIfCancellationRequested();
        var count = ns.GetTypeMembers().Count(type => MatchesKind(type, kind) && type.Locations.Any(location =>
            location.IsInSource && location.SourceTree is not null && sourceTrees.Contains(location.SourceTree)));
        foreach (var child in ns.GetNamespaceMembers()) count += CountProjectSourceTypesInHierarchy(child, sourceTrees, ct, kind);

        return count;
    }

    private static bool MatchesKind(INamedTypeSymbol type, string kind) => kind switch
    {
        "all" => true,
        "class" => type.TypeKind == TypeKind.Class && !type.IsRecord,
        "record" => type.IsRecord,
        "struct" => type.TypeKind == TypeKind.Struct && !type.IsRecord,
        "interface" => type.TypeKind == TypeKind.Interface,
        "enum" => type.TypeKind == TypeKind.Enum,
        "delegate" => type.TypeKind == TypeKind.Delegate,
        _ => false,
    };

    private static NamespaceNode? FindNode(IReadOnlyList<NamespaceNode> nodes, string fullName)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullName, fullName, StringComparison.Ordinal)) return node;
            var child = FindNode(node.Children, fullName);
            if (child is not null) return child;
        }
        return null;
    }

    private static NamespaceNode PruneAtDepth(NamespaceNode source, int maxDepth, int depth, out bool truncated)
    {
        truncated = false;
        var result = new NamespaceNode(source.Name, source.FullName, source.TypeCount);
        if (depth + 1 >= maxDepth)
        {
            truncated = source.Children.Count > 0;
            return result;
        }

        foreach (var child in source.Children)
        {
            var pruned = PruneAtDepth(child, maxDepth, depth + 1, out var childTruncated);
            result.Children.Add(pruned);
            truncated |= childTruncated;
        }
        return result;
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

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = i == 0 ? part : $"{currentPath}.{part}";

                if (!nodeMap.TryGetValue(currentPath, out var node))
                {
                    node = new NamespaceNode(part, currentPath, i == parts.Length - 1 ? count : 0);
                    nodeMap[currentPath] = node;
                    if (parent is null) roots.Add(node);
                    else parent.Children.Add(node);
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

    private static int CountNodes(IReadOnlyList<NamespaceNode> nodes) =>
        nodes.Sum(node => 1 + CountNodes(node.Children));

    private static List<NamespaceNode> TakeTree(
        IReadOnlyList<NamespaceNode> source,
        int budget,
        out int shownCount)
    {
        var result = new List<NamespaceNode>();
        shownCount = 0;
        foreach (var node in source.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            if (shownCount >= budget) break;
            shownCount++;
            var copy = new NamespaceNode(node.Name, node.FullName, node.TypeCount);
            result.Add(copy);
            if (shownCount < budget)
            {
                var children = TakeTree(node.Children, budget - shownCount, out var childCount);
                shownCount += childCount;
                copy.Children.AddRange(children);
            }
        }

        return result;
    }

    private static NamespaceTreePayload CreatePayload(
        string solutionName,
        string? projectName,
        IReadOnlyList<NamespaceNode> rootNodes,
        int totalNamespaces,
        int totalTypes,
        bool truncated,
        IReadOnlyList<string> truncatedBy,
        string? error,
        NamespaceTreeScanOptions requestedOptions,
        int effectiveDepth,
        int effectiveResults,
        bool boundsWereClamped,
        string? formatted = null,
        int? shownNamespaces = null)
    {
        var actualShown = shownNamespaces ?? CountNodes(rootNodes);
        return new NamespaceTreePayload(
            SolutionName: solutionName,
            ProjectName: projectName,
            RootNamespaces: rootNodes,
            TotalNamespaces: totalNamespaces,
            TotalTypes: totalTypes,
            FormattedText: formatted ?? FormatError(solutionName, projectName, error!),
            ShownNamespaces: actualShown,
            Truncated: truncated,
            TruncatedBy: truncatedBy,
            Error: error,
            RequestedMaxDepth: requestedOptions.MaxDepth,
            EffectiveMaxDepth: effectiveDepth,
            RequestedMaxResults: requestedOptions.MaxResults,
            EffectiveMaxResults: effectiveResults,
            BoundsWereClamped: boundsWereClamped,
            NextAction: GetNextAction(truncatedBy),
            IncludeGenerated: requestedOptions.IncludeGenerated);
    }

    private static string? GetNextAction(IReadOnlyList<string> truncatedBy)
    {
        if (truncatedBy.Count == 0) return null;
        if (truncatedBy.Contains("maxResults", StringComparer.Ordinal))
        {
            return $"Increase MaxResults (up to {MaxResultsCap}) or select a single project.";
        }

        return "Select a single project to narrow the namespace tree.";
    }

    private static string FormatError(string solutionName, string? projectName, string error)
    {
        var title = string.IsNullOrEmpty(projectName)
            ? $"# Namespace Tree: {solutionName}"
            : $"# Namespace Tree: {projectName} ({solutionName})";
        return $"{title}{Environment.NewLine}{Environment.NewLine}{error}";
    }

    private static string FormatTree(
        string solutionName,
        string? projectName,
        List<NamespaceNode> rootNodes,
        int totalNamespaces,
        int shownNamespaces,
        int totalTypes,
        bool includeGenerated,
        IReadOnlyList<string> truncatedBy,
        string? nextAction)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrEmpty(projectName)
            ? $"# Namespace Tree: {solutionName}"
            : $"# Namespace Tree: {projectName} ({solutionName})";

        sb.AppendLine(title);
        sb.AppendLine($"> {totalNamespaces} namespaces total, {shownNamespaces} shown | {totalTypes} types");
        sb.AppendLine($"> Generated source: {(includeGenerated ? "included" : "excluded")}");
        if (truncatedBy.Count > 0)
        {
            sb.AppendLine($"> Truncated by: {string.Join(", ", truncatedBy)}");
            sb.AppendLine($"> Next step: {nextAction}");
        }
        sb.AppendLine();

        foreach (var root in rootNodes) AppendNode(sb, root, 0);
        return sb.ToString().TrimEnd();
    }

    private static void AppendNode(StringBuilder sb, NamespaceNode node, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var typeInfo = node.TypeCount > 0 ? $" ({node.TypeCount} types)" : "";
        sb.AppendLine($"{indentStr}- {node.Name}{typeInfo}");
        foreach (var child in node.Children.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            AppendNode(sb, child, indent + 1);
        }
    }
}
