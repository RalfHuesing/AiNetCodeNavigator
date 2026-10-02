using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.CallTree;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

/// <summary>Expands and projects a call graph across the source owners in an assembly reference closure.</summary>
internal static class AssemblyCallTreeClosureBuilder
{
    internal sealed record BuildResult(
        CallGraphPayload Graph,
        bool TraversalLimited,
        bool ClosureIncomplete,
        IReadOnlyList<string> Diagnostics);

    private sealed record OwnerGraph(string TargetPath, AssemblyNavigationSessionScope Scope, CallGraphPayload Graph, string RootKey);
    private sealed record SourceOwner(string TargetPath, AssemblyNavigationSessionScope Scope);
    private sealed record ResolvedNodeOwner(
        string TargetPath,
        AssemblyNavigationSessionScope Scope,
        string DeclarationId,
        ISymbol Symbol,
        string HandoffId);
    private sealed record MergedGraph(
        Dictionary<string, CallGraphNode> Nodes,
        List<string> NodeOrder,
        List<(string FromKey, string ToKey, CallGraphEdge Edge)> Edges,
        List<(string CallerKey, UnresolvedCallSiteInfo Site)> UnresolvedCallSites,
        string RootKey,
        int HiddenEdges,
        bool LocalTruncated,
        CallGraphMethodHint[] MethodHints,
        int PendingNodes,
        bool TraversalLimited,
        bool ClosureIncomplete);

    internal static async Task<BuildResult> BuildAsync(
        AssemblyReferenceClosureSession session,
        string internalRootHandoff,
        int depth,
        int topN,
        CallTreeDirection direction,
        bool includeBcl,
        SymbolScopeType scope,
        bool includeGenerated,
        CancellationToken ct)
    {
        var handoffOwnerPath = session.HandoffOwnerPath;
        var declarationId = session.DeclarationCommentId;
        var sourceOwners = session.Owners.Select(owner => new SourceOwner(owner.TargetPath, owner.Scope)).ToList();
        var rootKey = AssemblyCallTreeNodeKey(handoffOwnerPath, declarationId);
        var expansion = await ExpandOwnersAsync(session, sourceOwners, rootKey, depth, topN, direction, includeBcl,
            scope, includeGenerated, ct).ConfigureAwait(false);
        var closureIncomplete = session.OwnerLimitReached || session.HasFailedOwners || session.HasUnresolvedReferences;
        var merged = MergeOwnerGraphs(expansion.Owners, sourceOwners, rootKey, handoffOwnerPath, declarationId,
            internalRootHandoff, expansion.TraversalLimited, closureIncomplete);
        var graph = ProjectGlobalGraph(merged, direction, depth, topN);
        return new(graph, expansion.TraversalLimited, closureIncomplete,
            expansion.Owners.SelectMany(owner => owner.Scope.Context.Diagnostics).ToArray());
    }

    private sealed record ExpansionResult(IReadOnlyList<OwnerGraph> Owners, bool TraversalLimited);

    private static async Task<ExpansionResult> ExpandOwnersAsync(
        AssemblyReferenceClosureSession session,
        IReadOnlyList<SourceOwner> sourceOwners,
        string rootKey,
        int depth,
        int topN,
        CallTreeDirection direction,
        bool includeBcl,
        SymbolScopeType scope,
        bool includeGenerated,
        CancellationToken ct)
    {
        var owners = new List<OwnerGraph>();
        var frontierQueue = new Queue<(string OwnerPath, AssemblyIdentityDto Identity, string DeclarationId, int Depth)>();
        var visitedFrontiers = new HashSet<string>(StringComparer.Ordinal) { rootKey };
        frontierQueue.Enqueue((session.HandoffOwnerPath, session.HandoffIdentity, session.DeclarationCommentId, 0));
        var traversalLimited = false;
        while (frontierQueue.TryDequeue(out var frontier))
        {
            ct.ThrowIfCancellationRequested();
            if (frontier.Depth >= Math.Clamp(depth, 1, 3)) continue;
            var frontierKey = AssemblyCallTreeNodeKey(frontier.OwnerPath, frontier.DeclarationId);
            foreach (var scanOwner in sourceOwners)
            {
                ct.ThrowIfCancellationRequested();
                var targetSymbol = string.Equals(scanOwner.TargetPath, frontier.OwnerPath, StringComparison.OrdinalIgnoreCase)
                    && AssemblyIdentityMatcher.Matches(scanOwner.Scope.Context.Identity, frontier.Identity)
                    ? ResolveAssemblySourceSymbolInOwner(frontier.DeclarationId, scanOwner.Scope)
                    : ResolveAssemblySymbolInCompilation(frontier.DeclarationId, frontier.Identity, scanOwner.Scope.Context.Compilation);
                if (targetSymbol is null) continue;

                CallGraphPayload graph;
                try
                {
                    graph = await CallTreeBuilder.BuildGraphAsync(new CallTreeBuildRequest(
                        scanOwner.Scope.Solution, targetSymbol, 1, topN, direction, includeBcl, scope, includeGenerated,
                        AssemblyHandoffFormatting.CreateInternal(scanOwner.Scope.Solution, scanOwner.Scope.Context)), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    traversalLimited = true;
                    continue;
                }
                owners.Add(new(scanOwner.TargetPath, scanOwner.Scope, graph, frontierKey));

                foreach (var node in graph.Nodes)
                {
                    if (string.Equals(node.NodeId, graph.RootNodeId, StringComparison.Ordinal)) continue;
                    var resolvedOwner = ResolveAssemblyCallTreeNodeOwner(node, sourceOwners);
                    if (resolvedOwner is null)
                    {
                        if (frontier.Depth + 1 < Math.Clamp(depth, 1, 3)
                            && node.ContainingAssemblyIdentity is { } unresolvedIdentity
                            && sourceOwners.Any(candidate => AssemblyIdentityMatcher.Matches(candidate.Scope.Context.Identity, unresolvedIdentity)))
                            traversalLimited = true;
                        continue;
                    }
                    var nextKey = AssemblyCallTreeNodeKey(resolvedOwner.TargetPath, resolvedOwner.DeclarationId);
                    if (!visitedFrontiers.Add(nextKey)) continue;
                    if (visitedFrontiers.Count >= CallTreeBuilder.MaxCallTreeNodes)
                    {
                        traversalLimited = true;
                        continue;
                    }
                    frontierQueue.Enqueue((resolvedOwner.TargetPath, resolvedOwner.Scope.Context.Identity!,
                        resolvedOwner.DeclarationId, frontier.Depth + 1));
                }
            }
        }

        return new(owners, traversalLimited);
    }

    private static MergedGraph MergeOwnerGraphs(
        IReadOnlyList<OwnerGraph> owners,
        IReadOnlyList<SourceOwner> sourceOwners,
        string rootKey,
        string handoffOwnerPath,
        string declarationId,
        string internalRootHandoff,
        bool traversalLimited,
        bool closureIncomplete)
    {
        // Merge owner graphs by full owner path and declaration identity before applying global limits.
        var nodeMap = new Dictionary<string, CallGraphNode>(StringComparer.Ordinal);
        var nodeOrder = new List<string>();
        var partKeys = new List<Dictionary<string, string>>();
        CallGraphMethodHint[] methodHints = [];
        var hiddenEdges = 0;
        var localTruncated = false;
        foreach (var owner in owners)
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in owner.Graph.Nodes)
            {
                var isRoot = string.Equals(node.NodeId, owner.Graph.RootNodeId, StringComparison.Ordinal);
                var resolvedNodeOwner = isRoot ? null : ResolveAssemblyCallTreeNodeOwner(node, sourceOwners);
                var nodeHandoff = isRoot ? internalRootHandoff : resolvedNodeOwner?.HandoffId;
                var nodeOwnerPath = isRoot ? handoffOwnerPath : resolvedNodeOwner?.TargetPath;
                var declaration = isRoot ? declarationId : resolvedNodeOwner?.DeclarationId;
                var key = isRoot ? owner.RootKey : declaration is not null && nodeOwnerPath is not null
                    ? AssemblyCallTreeNodeKey(nodeOwnerPath, declaration)
                    : owner.TargetPath + "|" + node.SymbolId + "|" + node.NodeId;
                keys[node.NodeId] = key;
                var visibleNode = node with { OwnerTargetPath = nodeOwnerPath, HandoffId = nodeHandoff };
                if (!nodeMap.TryGetValue(key, out var prior))
                {
                    nodeMap.Add(key, visibleNode);
                    nodeOrder.Add(key);
                }
                else if (string.IsNullOrWhiteSpace(prior.DisplayLine) && !string.IsNullOrWhiteSpace(visibleNode.DisplayLine)
                    || prior.HandoffId is null && visibleNode.HandoffId is not null)
                {
                    nodeMap[key] = prior with
                    {
                        DisplayLine = string.IsNullOrWhiteSpace(prior.DisplayLine) ? visibleNode.DisplayLine : prior.DisplayLine,
                        HandoffId = prior.HandoffId ?? visibleNode.HandoffId,
                        OwnerTargetPath = prior.OwnerTargetPath ?? visibleNode.OwnerTargetPath,
                    };
                }
            }
            partKeys.Add(keys);
            hiddenEdges += owner.Graph.HiddenEdgeCount;
            localTruncated |= owner.Graph.Truncated;
            if (methodHints.Length == 0 && owner.Graph.MethodHints is { Count: > 0 }) methodHints = owner.Graph.MethodHints.ToArray();
        }

        if (nodeMap.TryGetValue(rootKey, out var rootNode))
            nodeMap[rootKey] = rootNode with { HandoffId = internalRootHandoff, OwnerTargetPath = handoffOwnerPath };
        var mergedEdgesByKey = new List<(string FromKey, string ToKey, CallGraphEdge Edge)>();
        var unresolvedCallSites = new List<(string CallerKey, UnresolvedCallSiteInfo Site)>();
        foreach (var (partIndex, owner) in owners.Select((owner, index) => (index, owner)))
        {
            var keys = partKeys[partIndex];
            foreach (var site in owner.Graph.UnresolvedCallSites ?? [])
            {
                if (keys.TryGetValue(site.CallerNodeId, out var callerKey))
                    unresolvedCallSites.Add((callerKey, site));
            }
            foreach (var edge in owner.Graph.Edges)
            {
                if (!keys.TryGetValue(edge.FromNodeId, out var fromKey) || !keys.TryGetValue(edge.ToNodeId, out var toKey))
                {
                    hiddenEdges++;
                    continue;
                }
                var existingIndex = mergedEdgesByKey.FindIndex(item => item.FromKey == fromKey && item.ToKey == toKey
                    && string.Equals(item.Edge.DispatchKind, edge.DispatchKind, StringComparison.Ordinal));
                if (existingIndex < 0) mergedEdgesByKey.Add((fromKey, toKey, edge));
                else
                {
                    var existing = mergedEdgesByKey[existingIndex];
                    mergedEdgesByKey[existingIndex] = (fromKey, toKey, existing.Edge with
                    {
                        CallSites = existing.Edge.CallSites.Concat(edge.CallSites).Distinct()
                            .OrderBy(site => site.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(site => site.Line).ThenBy(site => site.Column).ToArray()
                    });
                }
            }
        }

        return new(nodeMap, nodeOrder, mergedEdgesByKey, unresolvedCallSites, rootKey, hiddenEdges, localTruncated, methodHints,
            owners.Sum(owner => owner.Graph.PendingNodeCount), traversalLimited, closureIncomplete);
    }

    private static CallGraphPayload ProjectGlobalGraph(MergedGraph merged, CallTreeDirection direction, int depth, int topN)
    {
        var nodeMap = merged.Nodes;
        var nodeOrder = merged.NodeOrder;
        var rootKey = merged.RootKey;
        var mergedEdgesByKey = merged.Edges;
        var hiddenEdges = merged.HiddenEdges;
        var unresolvedCallSitesToProject = merged.UnresolvedCallSites.Take(CallTreeBuilder.MaxCallTreeNodes).ToArray();
        hiddenEdges += merged.UnresolvedCallSites.Count - unresolvedCallSitesToProject.Length;
        var cappedFanout = ApplyGlobalProjection(mergedEdgesByKey, rootKey, direction, depth, topN);
        mergedEdgesByKey = cappedFanout.Edges;
        hiddenEdges += cappedFanout.HiddenCount;
        if (mergedEdgesByKey.Count > CallTreeBuilder.MaxCallTreeNodes)
        {
            hiddenEdges += mergedEdgesByKey.Count - CallTreeBuilder.MaxCallTreeNodes;
            mergedEdgesByKey = mergedEdgesByKey.Take(CallTreeBuilder.MaxCallTreeNodes).ToList();
        }

        var visibleNodeKeys = mergedEdgesByKey.SelectMany(edge => new[] { edge.FromKey, edge.ToKey })
            .Concat(unresolvedCallSitesToProject.Select(site => site.CallerKey))
            .Append(rootKey).ToHashSet(StringComparer.Ordinal);
        var projectedNodeKeys = nodeOrder.Where(visibleNodeKeys.Contains).ToArray();
        var selectedNodeKeys = projectedNodeKeys.Take(CallTreeBuilder.MaxCallTreeNodes).ToArray();
        if (projectedNodeKeys.Length > selectedNodeKeys.Length)
        {
            hiddenEdges += mergedEdgesByKey.Count(edge => !selectedNodeKeys.Contains(edge.FromKey, StringComparer.Ordinal)
                || !selectedNodeKeys.Contains(edge.ToKey, StringComparer.Ordinal));
            mergedEdgesByKey = mergedEdgesByKey.Where(edge => selectedNodeKeys.Contains(edge.FromKey, StringComparer.Ordinal)
                && selectedNodeKeys.Contains(edge.ToKey, StringComparer.Ordinal)).ToList();
        }
        var selectedNodeIds = selectedNodeKeys.Select((key, index) => (key, id: $"n{index + 1}"))
            .ToDictionary(item => item.key, item => item.id, StringComparer.Ordinal);
        var outputNodes = selectedNodeKeys.Select(key =>
        {
            var node = nodeMap[key];
            return node with { NodeId = selectedNodeIds[key], HandoffId = AssemblyHandoffFormatting.Externalize(node.HandoffId) };
        }).ToArray();
        var mergedEdges = mergedEdgesByKey.Where(edge => selectedNodeIds.ContainsKey(edge.FromKey) && selectedNodeIds.ContainsKey(edge.ToKey))
            .Select(edge => edge.Edge with { FromNodeId = selectedNodeIds[edge.FromKey], ToNodeId = selectedNodeIds[edge.ToKey] })
            .ToArray();
        var unresolvedCallSites = unresolvedCallSitesToProject
            .Where(item => selectedNodeIds.ContainsKey(item.CallerKey))
            .Select(item => item.Site with { CallerNodeId = selectedNodeIds[item.CallerKey] })
            .ToArray();

        var truncated = merged.LocalTruncated || merged.ClosureIncomplete || merged.TraversalLimited
            || nodeOrder.Count > selectedNodeKeys.Length || hiddenEdges > 0;
        return new CallGraphPayload(selectedNodeKeys.Length > 0 ? selectedNodeIds[selectedNodeKeys[0]] : string.Empty,
            outputNodes, mergedEdges, merged.MethodHints, truncated, hiddenEdges, merged.PendingNodes,
            unresolvedCallSites.Length == 0 ? null : unresolvedCallSites);
    }

    private static (List<(string FromKey, string ToKey, CallGraphEdge Edge)> Edges, int HiddenCount) ApplyGlobalProjection(
        List<(string FromKey, string ToKey, CallGraphEdge Edge)> edges, string rootKey, CallTreeDirection direction, int depth, int topN)
    {
        var kept = new HashSet<int>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootKey };
        var pending = new Queue<(string NodeKey, int Level)>();
        pending.Enqueue((rootKey, 0));
        var hidden = 0;
        while (pending.TryDequeue(out var current))
        {
            if (current.Level >= depth) continue;
            var incoming = direction is CallTreeDirection.Incoming or CallTreeDirection.Both
                ? edges.Select((edge, index) => (edge, index)).Where(item => item.edge.ToKey == current.NodeKey && !kept.Contains(item.index)).ToArray()
                : [];
            var remaining = topN;
            if (direction is CallTreeDirection.Incoming or CallTreeDirection.Both)
            {
                foreach (var item in incoming.Take(remaining))
                {
                    kept.Add(item.index);
                    remaining--;
                    EnqueueOther(current.NodeKey, current.Level, item.edge);
                }
                hidden += Math.Max(0, incoming.Length - topN);
            }
            if (direction is CallTreeDirection.Outgoing or CallTreeDirection.Both)
            {
                var outgoing = edges.Select((edge, index) => (edge, index))
                    .Where(item => item.edge.FromKey == current.NodeKey && !kept.Contains(item.index)).ToArray();
                foreach (var item in outgoing.Take(remaining))
                {
                    if (!kept.Add(item.index)) continue;
                    EnqueueOther(current.NodeKey, current.Level, item.edge);
                }
                hidden += Math.Max(0, outgoing.Length - remaining);
            }
        }
        return (edges.Where((_, index) => kept.Contains(index)).ToList(), hidden);

        void EnqueueOther(string currentNodeKey, int currentLevel, (string FromKey, string ToKey, CallGraphEdge Edge) edge)
        {
            var other = edge.FromKey == currentNodeKey ? edge.ToKey : edge.FromKey;
            if (visited.Add(other)) pending.Enqueue((other, currentLevel + 1));
        }
    }

    private static ISymbol? ResolveAssemblySymbolInCompilation(string declarationId, AssemblyIdentityDto sourceIdentity, Compilation compilation)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
            .Where(symbol => symbol.ContainingAssembly is { } assembly && AssemblyIdentityMatcher.Matches(assembly.Identity, sourceIdentity))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ISymbol? ResolveAssemblySourceSymbolInOwner(string declarationId, AssemblyNavigationSessionScope ownerScope)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, ownerScope.Context.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, ownerScope.Context.Assembly)
                && AssemblyHandoffFormatting.HasSourceDeclaration(symbol, ownerScope.Solution,
                    ownerScope.Context.DecompiledProjectPaths?.DecompiledSourceRoot))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ResolvedNodeOwner? ResolveAssemblyCallTreeNodeOwner(CallGraphNode node, IReadOnlyList<SourceOwner> sourceOwners)
    {
        if (string.IsNullOrWhiteSpace(node.SymbolId)) return null;
        SymbolHandoffIdentifier? handoff = SymbolHandoffIdentifier.TryParse(node.HandoffId ?? string.Empty, out var parsed)
            && parsed.Origin is SymbolHandoffOrigin.Assembly ? parsed : null;
        var declarationId = handoff?.DocumentationCommentId ?? node.SymbolId;
        var matches = new List<ResolvedNodeOwner>();
        foreach (var candidate in sourceOwners)
        {
            if (handoff is null && (node.ContainingAssemblyIdentity is not { } containingIdentity
                || !AssemblyIdentityMatcher.Matches(candidate.Scope.Context.Identity, containingIdentity))) continue;
            var symbol = ResolveAssemblySourceSymbolInOwner(declarationId, candidate.Scope);
            if (symbol is null) continue;
            var formatter = AssemblyHandoffFormatting.CreateInternal(candidate.Scope.Solution, candidate.Scope.Context);
            var internalHandoff = formatter(symbol);
            if (internalHandoff is null || handoff is not null
                && !string.Equals(internalHandoff, node.HandoffId, StringComparison.Ordinal)) continue;
            matches.Add(new(candidate.TargetPath, candidate.Scope, declarationId, symbol, internalHandoff));
            if (matches.Count > 1) return null;
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string AssemblyCallTreeNodeKey(string ownerPath, string declarationId) =>
        Path.GetFullPath(ownerPath).ToUpperInvariant() + "\0" + declarationId;
}
