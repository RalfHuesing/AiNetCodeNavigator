#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.CallTree;

/// <summary>
/// Traversiert den Aufrufbaum eines Symbols in Roslyn (eingehend, ausgehend oder beides).
/// </summary>
public static class CallTreeBuilder
{
    public const int MaxCallTreeDepth = 5;
    public const int MaxCallTreeNodes = 250;

    public static async Task<CallGraphPayload> BuildGraphAsync(
        CallTreeBuildRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Solution);
        ArgumentNullException.ThrowIfNull(request.SeedSymbol);

        var depth = Math.Clamp(request.RequestedDepth, 1, MaxCallTreeDepth);
        var solutionDir = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(request.Solution, ct).ConfigureAwait(false);

        var state = new BuilderState(request.Solution, solutionDir, depth, Math.Max(request.TopN, 1), request.IncludeBcl, handoffIdentity, request.Scope, request.IncludeGenerated);

        if (request.SeedSymbol is INamedTypeSymbol namedType)
        {
            return state.CreateTypeSeedPayload(namedType);
        }

        var rootNode = state.GetOrAddNode(request.SeedSymbol);
        state.Enqueue(request.SeedSymbol, 1);

        while (state.HasQueuedNodes && !state.IsAtNodeCap)
        {
            ct.ThrowIfCancellationRequested();
            var (currentSymbol, currentLevel) = state.Dequeue();
            if (currentLevel > depth) continue;

            var remainingFanOut = state.TopN;
            if (request.Direction is CallTreeDirection.Incoming or CallTreeDirection.Both)
            {
                remainingFanOut -= await ExpandIncomingAsync(state, currentSymbol, currentLevel, remainingFanOut, ct).ConfigureAwait(false);
            }

            if (request.Direction is CallTreeDirection.Outgoing or CallTreeDirection.Both)
            {
                await ExpandOutgoingAsync(state, currentSymbol, currentLevel, remainingFanOut, ct).ConfigureAwait(false);
            }
        }

        return state.CreatePayload();
    }

    private static async Task<int> ExpandIncomingAsync(
        BuilderState state,
        ISymbol targetSymbol,
        int level,
        int fanOutBudget,
        CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(targetSymbol, state.Solution, ct).ConfigureAwait(false);
        var callerGroups = new Dictionary<ISymbol, List<CallSiteInfo>>(SymbolEqualityComparer.Default);

        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var loc in reference.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (loc.Document is not { } doc) continue;
                var isTest = TestDetector.IsTestProject(doc.Project) || TestDetector.IsTestFile(doc.FilePath);
                if ((state.Scope == SymbolScopeType.Production && isTest) || (state.Scope == SymbolScopeType.Tests && !isTest)) continue;
                if (!state.IncludeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(doc, ct).ConfigureAwait(false)) continue;

                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var enclosing = semanticModel?.GetEnclosingSymbol(loc.Location.SourceSpan.Start);
                if (enclosing is null) continue;

                // Resolve caller to method or property
                var caller = enclosing;
                while (caller is not null and not (IMethodSymbol or IPropertySymbol))
                {
                    caller = caller.ContainingSymbol;
                }
                if (caller is null) caller = enclosing;

                // Skip self-declarations
                if (SymbolEqualityComparer.Default.Equals(caller, targetSymbol)) continue;

                var lineSpan = loc.Location.GetLineSpan();
                var relPath = PathNormalizer.ToRelative(state.SolutionDir, lineSpan.Path);
                var line = lineSpan.StartLinePosition.Line + 1;

                if (!callerGroups.TryGetValue(caller, out var list))
                {
                    list = [];
                    callerGroups[caller] = list;
                }

                list.Add(new CallSiteInfo(relPath, line));
            }
        }

        var sortedCallers = callerGroups
            .OrderBy(g => g.Value.FirstOrDefault()?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Value.FirstOrDefault()?.Line ?? 0)
            .ToList();

        var shown = sortedCallers.Take(fanOutBudget).ToList();
        if (sortedCallers.Count > shown.Count)
        {
            state.AddHiddenEdges(sortedCallers.Count - shown.Count);
        }

        foreach (var (caller, callSites) in shown)
        {
            if (!state.TryGetOrAddNode(caller, out var callerNode)
                || !state.TryGetOrAddNode(targetSymbol, out var targetNode))
            {
                state.AddHiddenEdges(1);
                continue;
            }

            state.AddEdge(callerNode.NodeId, targetNode.NodeId, callSites);

            if (level < state.MaxDepth && state.MarkVisited(caller))
            {
                state.Enqueue(caller, level + 1);
            }
        }

        return shown.Count;
    }

    private static async Task<int> ExpandOutgoingAsync(
        BuilderState state,
        ISymbol sourceSymbol,
        int level,
        int fanOutBudget,
        CancellationToken ct)
    {
        var calleeGroups = new Dictionary<ISymbol, List<CallSiteInfo>>(SymbolEqualityComparer.Default);

        foreach (var syntaxRef in sourceSymbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            var syntax = await syntaxRef.GetSyntaxAsync(ct).ConfigureAwait(false);
            var doc = state.Solution.GetDocument(syntax.SyntaxTree);
            if (doc is null) continue;
            var isTestDocument = TestDetector.IsTestProject(doc.Project) || TestDetector.IsTestFile(doc.FilePath);
            if ((state.Scope == SymbolScopeType.Production && isTestDocument) || (state.Scope == SymbolScopeType.Tests && !isTestDocument)) continue;
            if (!state.IncludeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(doc, ct).ConfigureAwait(false)) continue;

            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel is null) continue;

            var body = GetBodyNode(syntax);
            if (body is null) continue;

            void AddTarget(ISymbol? target, SyntaxNode callSite)
            {
                if (target is null) return;

                var isExternal = !target.Locations.Any(location => location.IsInSource);
                if (!state.IncludeBcl && isExternal && IsBclSymbol(target)) return;

                var lineSpan = callSite.GetLocation().GetLineSpan();
                var relPath = PathNormalizer.ToRelative(state.SolutionDir, lineSpan.Path);
                var line = lineSpan.StartLinePosition.Line + 1;

                if (!calleeGroups.TryGetValue(target, out var list))
                {
                    list = [];
                    calleeGroups[target] = list;
                }

                list.Add(new CallSiteInfo(relPath, line));
            }

            foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
                var target = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (target is null && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    target = ResolveMemberAccess(memberAccess, semanticModel, ct);
                }
                AddTarget(target, invocation);
            }

            foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(creation, ct);
                AddTarget(symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()
                    ?? semanticModel.GetTypeInfo(creation, ct).Type, creation);
            }

            foreach (var creation in body.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(creation, ct);
                AddTarget(symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()
                    ?? semanticModel.GetTypeInfo(creation, ct).Type, creation);
            }

            foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Parent is InvocationExpressionSyntax) continue;
                AddTarget(ResolveMemberAccess(memberAccess, semanticModel, ct), memberAccess);
            }
        }

        var sortedCallees = calleeGroups
            .OrderBy(g => g.Value.FirstOrDefault()?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Value.FirstOrDefault()?.Line ?? 0)
            .ToList();

        var shown = sortedCallees.Take(fanOutBudget).ToList();
        if (sortedCallees.Count > shown.Count)
        {
            state.AddHiddenEdges(sortedCallees.Count - shown.Count);
        }

        foreach (var (callee, callSites) in shown)
        {
            if (!state.TryGetOrAddNode(sourceSymbol, out var sourceNode)
                || !state.TryGetOrAddNode(callee, out var calleeNode))
            {
                state.AddHiddenEdges(1);
                continue;
            }

            state.AddEdge(sourceNode.NodeId, calleeNode.NodeId, callSites);

            if (level < state.MaxDepth && state.MarkVisited(callee))
            {
                state.Enqueue(callee, level + 1);
            }
        }

        return shown.Count;
    }

    private static bool IsBclSymbol(ISymbol symbol)
    {
        var assemblyName = symbol.ContainingAssembly?.Name;
        if (!string.IsNullOrEmpty(assemblyName)
            && (string.Equals(assemblyName, "System", StringComparison.OrdinalIgnoreCase)
                || assemblyName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                || assemblyName.StartsWith("Microsoft.NETCore.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assemblyName, "netstandard", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var namespaceName = symbol.ContainingNamespace?.ToDisplayString();
        return !string.IsNullOrEmpty(namespaceName)
            && (string.Equals(namespaceName, "System", StringComparison.Ordinal)
                || namespaceName.StartsWith("System.", StringComparison.Ordinal)
                || namespaceName.StartsWith("Microsoft.Win32", StringComparison.Ordinal));
    }

    private static ISymbol? ResolveMemberAccess(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel, CancellationToken ct)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, ct);
        if (symbolInfo.Symbol is not null) return symbolInfo.Symbol;
        if (symbolInfo.CandidateSymbols.Length > 0) return symbolInfo.CandidateSymbols[0];
        return semanticModel.GetMemberGroup(memberAccess, ct).FirstOrDefault();
    }

    private static SyntaxNode? GetBodyNode(SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
            PropertyDeclarationSyntax p => (SyntaxNode?)p.AccessorList ?? p.ExpressionBody,
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
            _ => node
        };

    private sealed class BuilderState
    {
        public Solution Solution { get; }
        public string SolutionDir { get; }
        public int MaxDepth { get; }
        public int TopN { get; }
        public bool IncludeBcl { get; }
        public AnalysisSymbolIdentity? HandoffIdentity { get; }

        private readonly Dictionary<ISymbol, CallGraphNode> _nodesBySymbol = new(SymbolEqualityComparer.Default);
        private readonly List<CallGraphNode> _nodes = [];
        private readonly List<CallGraphEdge> _edges = [];
        private readonly Queue<(ISymbol Symbol, int Level)> _queue = new();
        private readonly HashSet<ISymbol> _visited = new(SymbolEqualityComparer.Default);
        private int _hiddenEdgeCount;

        public SymbolScopeType Scope { get; }
        public bool IncludeGenerated { get; }

        public BuilderState(Solution solution, string solutionDir, int maxDepth, int topN, bool includeBcl, AnalysisSymbolIdentity? handoffIdentity, SymbolScopeType scope, bool includeGenerated)
        {
            Solution = solution;
            SolutionDir = solutionDir;
            MaxDepth = maxDepth;
            TopN = topN;
            IncludeBcl = includeBcl;
            HandoffIdentity = handoffIdentity;
            Scope = scope;
            IncludeGenerated = includeGenerated;
        }

        public bool HasQueuedNodes => _queue.Count > 0;
        public bool IsAtNodeCap => _nodes.Count >= MaxCallTreeNodes;

        public void Enqueue(ISymbol symbol, int level) => _queue.Enqueue((symbol, level));
        public (ISymbol Symbol, int Level) Dequeue() => _queue.Dequeue();

        public bool MarkVisited(ISymbol symbol) => _visited.Add(symbol);
        public void AddHiddenEdges(int count) => _hiddenEdgeCount += count;

        public CallGraphNode GetOrAddNode(ISymbol symbol)
        {
            if (_nodesBySymbol.TryGetValue(symbol, out var existing)) return existing;

            var nodeId = $"n{_nodes.Count + 1}";
            var name = FormatSymbolName(symbol);
            var displayLine = FormatDisplayLine(symbol, SolutionDir);
            var docCommentId = symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString();
            var handoffId = SourceHandoffFormatter.Format(symbol, Solution, HandoffIdentity);

            var node = new CallGraphNode(
                NodeId: nodeId,
                SymbolId: docCommentId,
                Name: name,
                DisplayLine: displayLine,
                Kind: symbol.Kind.ToString().ToLowerInvariant(),
                HandoffId: handoffId);

            _nodesBySymbol[symbol] = node;
            _nodes.Add(node);
            return node;
        }

        public bool TryGetOrAddNode(ISymbol symbol, out CallGraphNode node)
        {
            if (_nodesBySymbol.TryGetValue(symbol, out node!)) return true;
            if (_nodes.Count >= MaxCallTreeNodes)
            {
                node = null!;
                return false;
            }

            node = GetOrAddNode(symbol);
            return true;
        }

        public void AddEdge(string fromNodeId, string toNodeId, IReadOnlyList<CallSiteInfo> callSites)
        {
            var existing = _edges.FirstOrDefault(e => e.FromNodeId == fromNodeId && e.ToNodeId == toNodeId);
            if (existing != null)
            {
                var combined = existing.CallSites.Concat(callSites).Distinct().ToList();
                _edges.Remove(existing);
                _edges.Add(existing with { CallSites = combined });
            }
            else
            {
                _edges.Add(new CallGraphEdge(fromNodeId, toNodeId, callSites));
            }
        }

        public CallGraphPayload CreatePayload() =>
            new(
                RootNodeId: _nodes.Count > 0 ? _nodes[0].NodeId : string.Empty,
                Nodes: _nodes,
                Edges: _edges,
                Truncated: _queue.Count > 0 || _hiddenEdgeCount > 0,
                HiddenEdgeCount: _hiddenEdgeCount,
                PendingNodeCount: _queue.Count);

        public CallGraphPayload CreateTypeSeedPayload(INamedTypeSymbol namedType)
        {
            var rootNode = GetOrAddNode(namedType);
            var hints = namedType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Select(m => new CallGraphMethodHint(
                    Name: FormatSymbolName(m),
                    SymbolId: m.GetDocumentationCommentId() ?? m.ToDisplayString(),
                    DisplayLine: FormatDisplayLine(m, SolutionDir)))
                .Take(5)
                .ToList();

            return new CallGraphPayload(
                RootNodeId: rootNode.NodeId,
                Nodes: _nodes,
                Edges: _edges,
                MethodHints: hints,
                Truncated: false,
                HiddenEdgeCount: 0);
        }

        private static string FormatSymbolName(ISymbol symbol)
        {
            var typeName = symbol.ContainingType?.Name;
            if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
            {
                return typeName ?? symbol.Name;
            }

            return typeName != null ? $"{typeName}.{symbol.Name}" : symbol.Name;
        }

        private static string FormatDisplayLine(ISymbol symbol, string solutionDir)
        {
            var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (loc is null || loc.SourceTree is null) return string.Empty;

            var relPath = PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath);
            var line = loc.GetLineSpan().StartLinePosition.Line + 1;
            return $"{relPath}:{line}";
        }
    }
}
