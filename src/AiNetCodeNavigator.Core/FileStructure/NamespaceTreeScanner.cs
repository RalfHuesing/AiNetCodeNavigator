#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
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
        var allProjects = solution.Projects.ToList();
        IReadOnlyList<Project> namedProjects = allProjects;
        Project? prefixProject = null;

        if (requestedProjectName is not null)
        {
            Project[] selectedByPath = [];
            try
            {
                var requestedPath = Path.GetFullPath(Path.IsPathRooted(requestedProjectName)
                    ? requestedProjectName
                    : Path.Combine(Path.GetDirectoryName(solution.FilePath) ?? string.Empty, requestedProjectName));
                selectedByPath = allProjects.Where(project => !string.IsNullOrWhiteSpace(project.FilePath)
                    && PathEquals(project.FilePath!, requestedPath)).ToArray();
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }

            var exact = selectedByPath.Length > 0
                ? selectedByPath
                : allProjects.Where(project => string.Equals(project.Name, requestedProjectName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var candidates = exact.Length > 0 ? exact : allProjects.Where(project => project.Name.Contains(requestedProjectName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
                return CreatePayload(solutionName, requestedProjectName, [], 0, 0, false, [],
                    $"Project '{requestedProjectName}' was not found.", requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
            if (candidates.Length > 1)
                return CreateProjectSelectionError(solutionName, requestedProjectName, candidates, requestedOptions,
                    effectiveDepth, effectiveResults, boundsWereClamped);
            namedProjects = candidates;
        }
        else if (prefix is not null)
        {
            var prefixCandidates = new List<Project>();
            foreach (var candidate in allProjects.Where(project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal)))
            {
                var namespaceCounts = await GetNamespaceCountsAsync(candidate, requestedOptions.IncludeGenerated, ct).ConfigureAwait(false);
                if (namespaceCounts.Error is not null)
                    return CreatePayload(solutionName, null, [], 0, 0, false, [], namespaceCounts.Error,
                        requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
                if (FindNode(BuildTree(namespaceCounts.Counts!), prefix) is { } prefixNode
                    && CountTypeMembersInTree(prefixNode) > 0) prefixCandidates.Add(candidate);
            }
            if (prefixCandidates.Count == 0)
                return CreatePayload(solutionName, null, [], 0, 0, false, [], $"Namespace prefix '{prefix}' was not found in a C# project.",
                    requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped, errorCode: NavigationErrorCodes.SymbolNotFound);
            if (prefixCandidates.Count > 1)
                return CreateProjectSelectionError(solutionName, prefix, prefixCandidates, requestedOptions,
                    effectiveDepth, effectiveResults, boundsWereClamped, NavigationErrorCodes.AmbiguousSymbol);
            prefixProject = prefixCandidates[0];
            namedProjects = [prefixProject];
        }

        var selectedProject = requestedProjectName is null ? prefixProject : namedProjects[0];
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

        if (requestedOptions.IncludeProjectOverview && requestedProjectName is null && prefix is null)
            return await BuildProjectOverviewAsync(solution, solutionName, projects, requestedOptions, ct).ConfigureAwait(false);

        var nsTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var namespaceTypes = new Dictionary<string, List<NamespaceTypeEntry>>(StringComparer.Ordinal);
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
                    namespaceTypes,
                    ref totalTypes,
                    ref depthWasTruncated,
                    ct,
                    kind,
                    requestedOptions.IncludeTypes,
                    requestedOptions.FormatTypeHandoff);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = $"Namespace tree scan failed: {ex.Message}";
            return CreatePayload(solutionName, selectedProjectName, [], 0, 0, false, [], error,
                requestedOptions, effectiveDepth, effectiveResults, boundsWereClamped);
        }

        var fullTree = BuildTree(nsTypeCounts);
        if (requestedOptions.IncludeTypes) AttachTypes(fullTree, namespaceTypes);
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
        var typeLimitWasReached = requestedOptions.IncludeTypes && LimitTypeEntries(fullTree, effectiveResults);
        var totalNamespaces = CountNodes(fullTree);
        var rootNodes = TakeTree(fullTree, effectiveResults, out var shownNamespaces);
        var resultLimitWasReached = shownNamespaces < totalNamespaces;
        var truncatedBy = new List<string>(2);
        if (depthWasTruncated) truncatedBy.Add("maxDepth");
        if (resultLimitWasReached || typeLimitWasReached) truncatedBy.Add("maxResults");

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

    private static NamespaceTreePayload CreateProjectSelectionError(string solutionName, string query,
        IReadOnlyList<Project> projects, NamespaceTreeScanOptions options, int effectiveDepth, int effectiveResults,
        bool boundsWereClamped, string errorCode = NavigationErrorCodes.AmbiguousSymbol)
    {
        var candidates = string.Join(", ", projects.Select(project => string.IsNullOrWhiteSpace(project.FilePath)
            ? project.Name
            : $"{project.Name} ({project.FilePath})"));
        return CreatePayload(solutionName, query, [], 0, 0, false, [],
            $"The selection '{query}' matches multiple projects: {candidates}. Pass one project name or project path.",
            options, effectiveDepth, effectiveResults, boundsWereClamped, errorCode: errorCode);
    }

    private static async Task<(Dictionary<string, int>? Counts, string? Error)> GetNamespaceCountsAsync(Project project,
        bool includeGenerated, CancellationToken ct)
    {
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null) return (null, $"Project '{project.Name}' could not be compiled.");
        var sourceTrees = await GetProjectSourceTreesAsync(project, includeGenerated, ct).ConfigureAwait(false);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeCount = 0;
        var depthTruncated = false;
        CollectNamespacesAndTypes(compilation.GlobalNamespace, sourceTrees, [], MaxDepthCap, counts, new Dictionary<string, List<NamespaceTypeEntry>>(StringComparer.Ordinal),
            ref typeCount, ref depthTruncated, ct, "all", includeTypes: false);
        return (counts, null);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task<NamespaceTreePayload> BuildProjectOverviewAsync(
        Solution solution,
        string solutionName,
        IReadOnlyList<Project> projects,
        NamespaceTreeScanOptions requestedOptions,
        CancellationToken ct)
    {
        var projectCounts = new List<(Project Project, int NamespaceCount, int TypeCount)>();
        foreach (var project in projects.OrderBy(project => project.Name, StringComparer.Ordinal)
                     .ThenBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(project => project.Id.Id))
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null)
                return CreatePayload(solutionName, null, [], 0, 0, false, [],
                    $"Project '{project.Name}' could not be compiled.", requestedOptions, 1, 1, false);
            var sourceTrees = await GetProjectSourceTreesAsync(project, requestedOptions.IncludeGenerated, ct).ConfigureAwait(false);
            var namespaces = new Dictionary<string, int>(StringComparer.Ordinal);
            var typeCount = 0;
            var depthTruncated = false;
            CollectNamespacesAndTypes(compilation.GlobalNamespace, sourceTrees, [], MaxDepthCap, namespaces, new Dictionary<string, List<NamespaceTypeEntry>>(StringComparer.Ordinal),
                ref typeCount, ref depthTruncated, ct, "all", includeTypes: false);
            projectCounts.Add((project, CountNodes(BuildTree(namespaces)), typeCount));
        }

        var effectiveResults = ClampBound(requestedOptions.MaxResults, MaxResultsCap);
        var shown = projectCounts.Take(effectiveResults).ToList();
        var duplicateNames = projectCounts.GroupBy(entry => entry.Project.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var solutionDirectory = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var projectEntries = shown.Select(entry => new NamespaceProjectOverviewEntry(
            entry.Project.Name,
            entry.Project.Language,
            entry.NamespaceCount,
            entry.TypeCount,
            duplicateNames.Contains(entry.Project.Name) && !string.IsNullOrWhiteSpace(entry.Project.FilePath)
                ? Path.GetRelativePath(solutionDirectory, entry.Project.FilePath).Replace('\\', '/')
                : null)).ToArray();
        var truncated = projectCounts.Count > shown.Count;
        var truncatedBy = truncated ? new[] { "maxResults" } : Array.Empty<string>();
        var totalNamespaces = projectCounts.Sum(entry => entry.NamespaceCount);
        var totalTypes = projectCounts.Sum(entry => entry.TypeCount);
        var formatted = FormatProjectOverview(solutionName, projectEntries, projectCounts.Count, totalNamespaces, totalTypes,
            requestedOptions.IncludeGenerated, truncated);
        return new NamespaceTreePayload(
            SolutionName: solutionName,
            ProjectName: null,
            RootNamespaces: [],
            TotalNamespaces: totalNamespaces,
            TotalTypes: totalTypes,
            FormattedText: formatted,
            Truncated: truncated,
            TruncatedBy: truncatedBy,
            RequestedMaxDepth: 1,
            EffectiveMaxDepth: 1,
            RequestedMaxResults: requestedOptions.MaxResults,
            EffectiveMaxResults: effectiveResults,
            BoundsWereClamped: effectiveResults != requestedOptions.MaxResults,
            NextAction: truncated ? $"Increase MaxResults (up to {MaxResultsCap}) or select a project to drill into its namespaces." : null,
            IncludeGenerated: requestedOptions.IncludeGenerated,
            Projects: projectEntries,
            TotalProjects: projectCounts.Count);
    }

    private static string FormatProjectOverview(string solutionName, IReadOnlyList<NamespaceProjectOverviewEntry> projects,
        int totalProjects, int totalNamespaces, int totalTypes, bool includeGenerated, bool truncated)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Namespace Tree: {solutionName}");
        builder.AppendLine($"> {totalProjects} projects | {totalNamespaces} namespaces | {totalTypes} types | depth 1");
        builder.AppendLine($"> Generated source: {(includeGenerated ? "included" : "excluded")}");
        if (truncated)
            builder.AppendLine($"> Truncated by: maxResults | Next step: increase maxResults (up to {MaxResultsCap}) or select a project to drill into its namespaces.");
        builder.AppendLine();
        builder.AppendLine("Projects:");
        foreach (var project in projects)
        {
            builder.Append($"- {project.ProjectName} ({project.ProjectType}) — {project.NamespaceCount} namespaces, {project.TypeCount} types");
            if (project.ProjectPath is not null) builder.Append($" — {project.ProjectPath}");
            builder.AppendLine();
        }
        if (projects.Count > 0) builder.AppendLine();
        builder.AppendLine("Set `project` to one of these project names to inspect its namespace tree.");
        return builder.ToString().TrimEnd();
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
        Dictionary<string, List<NamespaceTypeEntry>> namespaceTypes,
        ref int totalTypes,
        ref bool depthWasTruncated,
        CancellationToken ct,
        string kind,
        bool includeTypes,
        Func<INamedTypeSymbol, string?>? formatTypeHandoff = null)
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
                nsTypeCounts[fullName] = nsTypeCounts.GetValueOrDefault(fullName) + matchingTypes.Length;
                totalTypes += matchingTypes.Length;
                if (includeTypes)
                {
                    if (!namespaceTypes.TryGetValue(fullName, out var entries)) namespaceTypes[fullName] = entries = [];
                    foreach (var type in matchingTypes)
                    {
                        var location = type.Locations.Where(candidate => candidate.IsInSource && candidate.SourceTree is not null
                                && sourceTrees.Contains(candidate.SourceTree))
                            .OrderBy(candidate => candidate.SourceTree!.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(candidate => candidate.SourceSpan.Start)
                            .FirstOrDefault();
                        if (location?.SourceTree is null) continue;
                        var line = location.SourceTree.GetLineSpan(location.SourceSpan).StartLinePosition.Line + 1;
                        entries.Add(new NamespaceTypeEntry(type.Name, GetTypeDisplayKind(type), location.SourceTree.FilePath ?? string.Empty, line,
                            formatTypeHandoff?.Invoke(type)));
                    }
                }
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

            CollectNamespacesAndTypes(childNs, sourceTrees, path, maxDepth, nsTypeCounts, namespaceTypes, ref totalTypes,
                ref depthWasTruncated, ct, kind, includeTypes, formatTypeHandoff);
        }
    }

    private static string GetTypeDisplayKind(INamedTypeSymbol type) => type.IsRecord
        ? type.TypeKind == TypeKind.Struct ? "record struct" : "record class"
        : type.TypeKind.ToString().ToLowerInvariant();

    private static int CountTypeMembersInTree(NamespaceNode node) =>
        node.TypeCount + node.Children.Sum(CountTypeMembersInTree);

    private static void AttachTypes(IReadOnlyList<NamespaceNode> nodes, IReadOnlyDictionary<string, List<NamespaceTypeEntry>> namespaceTypes)
    {
        foreach (var node in nodes)
        {
            if (namespaceTypes.TryGetValue(node.FullName, out var types))
                node.Types.AddRange(types.OrderBy(type => type.Name, StringComparer.Ordinal)
                    .ThenBy(type => type.FilePath, StringComparer.OrdinalIgnoreCase));
            AttachTypes(node.Children, namespaceTypes);
        }
    }

    private static bool LimitTypeEntries(IReadOnlyList<NamespaceNode> nodes, int maxResults)
    {
        var remaining = maxResults;
        return LimitTypeEntries(nodes, ref remaining);
    }

    private static bool LimitTypeEntries(IReadOnlyList<NamespaceNode> nodes, ref int remaining)
    {
        var truncated = false;
        foreach (var node in nodes.OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            node.Types.Sort((left, right) =>
            {
                var nameOrder = StringComparer.Ordinal.Compare(left.Name, right.Name);
                return nameOrder != 0 ? nameOrder : StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath);
            });
            if (node.Types.Count > remaining)
            {
                node.Types.RemoveRange(remaining, node.Types.Count - remaining);
                truncated = true;
            }
            remaining -= node.Types.Count;
            truncated |= LimitTypeEntries(node.Children, ref remaining);
        }
        return truncated;
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
        result.Types.AddRange(source.Types);
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
            copy.Types.AddRange(node.Types);
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
        int? shownNamespaces = null,
        string? errorCode = null)
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
            IncludeGenerated: requestedOptions.IncludeGenerated,
            ErrorCode: errorCode);
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
        foreach (var type in node.Types.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var handoff = type.HandoffId is null ? string.Empty : $" [handoff: {type.HandoffId}]";
            sb.AppendLine($"{indentStr}  - {type.Kind} {type.Name} ({type.FilePath}:{type.Line}){handoff}");
        }
        foreach (var child in node.Children.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            AppendNode(sb, child, indent + 1);
        }
    }
}
