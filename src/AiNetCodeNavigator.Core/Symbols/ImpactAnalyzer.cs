#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Berechnet den transitiven Blast Radius (Impact) bei Änderungen an einem C#-Symbol.
/// </summary>
public static class ImpactAnalyzer
{
    public const int MaxAllowedDepth = 3;
    public const int MaxNodes = 200;

    public static async Task<SymbolImpactPayload> AnalyzeSymbolImpactAsync(
        ISymbol symbol,
        Solution solution,
        int maxDepth = 3,
        int maxResults = 50,
        CancellationToken ct = default,
        int maxNodes = MaxNodes)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(solution);
        if (maxNodes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNodes), "The node limit must be at least one.");
        }

        var effectiveNodeLimit = Math.Min(maxNodes, MaxNodes);
        var effectiveResultLimit = Math.Max(maxResults, 1);
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var depth = Math.Clamp(maxDepth, 1, MaxAllowedDepth);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
        var queue = new Queue<(ISymbol Symbol, int Level)>();
        queue.Enqueue((symbol, 1));

        var allSites = new List<ImpactCallSiteEntry>();
        var maxDepthReached = 0;
        var totalNodesExplored = 0;

        while (queue.Count > 0 && totalNodesExplored < effectiveNodeLimit)
        {
            ct.ThrowIfCancellationRequested();
            var (currentSymbol, currentLevel) = queue.Dequeue();
            totalNodesExplored++;

            if (currentLevel > maxDepthReached)
            {
                maxDepthReached = currentLevel;
            }

            var references = await SymbolFinder.FindReferencesAsync(currentSymbol, solution, ct).ConfigureAwait(false);
            var reachedFromSymbolId = GetStableSymbolId(currentSymbol);
            var reachedFromSymbolHandoffId = SourceHandoffFormatter.Format(currentSymbol, solution, handoffIdentity);

            foreach (var reference in references)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var loc in reference.Locations)
                {
                    ct.ThrowIfCancellationRequested();
                    if (loc.Document is not { } doc) continue;

                    var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    var enclosing = semanticModel?.GetEnclosingSymbol(loc.Location.SourceSpan.Start);
                    if (enclosing is null) continue;

                    var caller = enclosing;
                    while (caller is not null and not (IMethodSymbol or IPropertySymbol))
                    {
                        caller = caller.ContainingSymbol;
                    }
                    if (caller is null) caller = enclosing;

                    var lineSpan = loc.Location.GetLineSpan();
                    var relPath = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
                    var line = lineSpan.StartLinePosition.Line + 1;

                    var callerName = caller switch
                    {
                        IMethodSymbol m => $"{m.ContainingType?.Name}.{m.Name}",
                        IPropertySymbol p => $"{p.ContainingType?.Name}.{p.Name}",
                        _ => caller.Name
                    };

                    var handoff = SourceHandoffFormatter.Format(caller, solution, handoffIdentity);

                    allSites.Add(new ImpactCallSiteEntry(
                        FilePath: relPath,
                        Line: line,
                        CallingMember: callerName,
                        CallingMemberHandoffId: handoff,
                        ProjectName: doc.Project.Name,
                        Depth: currentLevel,
                        ReachedFromSymbolId: reachedFromSymbolId,
                        ReachedFromSymbolHandoffId: reachedFromSymbolHandoffId));

                    if (currentLevel < depth && visited.Add(caller))
                    {
                        queue.Enqueue((caller, currentLevel + 1));
                    }
                }
            }
        }

        var distinctSites = allSites
            .GroupBy(s => (
                s.ProjectName,
                s.FilePath,
                s.Line,
                s.CallingMember,
                s.CallingMemberHandoffId,
                s.Depth,
                s.ReachedFromSymbolId,
                s.ReachedFromSymbolHandoffId))
            .Select(g => g.OrderBy(s => s.Depth).First())
            .OrderBy(s => s.Depth)
            .ThenBy(s => s.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FilePath, StringComparer.Ordinal)
            .ThenBy(s => s.Line)
            .ThenBy(s => s.CallingMember, StringComparer.Ordinal)
            .ThenBy(s => s.CallingMemberHandoffId, StringComparer.Ordinal)
            .ThenBy(s => s.ReachedFromSymbolId, StringComparer.Ordinal)
            .ThenBy(s => s.ReachedFromSymbolHandoffId, StringComparer.Ordinal)
            .ToList();

        var isTruncated = distinctSites.Count > effectiveResultLimit;
        var truncatedByNodeLimit = queue.Count > 0 && totalNodesExplored >= effectiveNodeLimit;
        var shownSites = distinctSites.Take(effectiveResultLimit).ToList();

        var affectedProjects = distinctSites
            .Select(s => s.ProjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var affectedFiles = distinctSites
            .Select(s => s.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SymbolImpactPayload(
            TargetSymbol: symbol.Name,
            TargetKind: symbol.Kind.ToString().ToLowerInvariant(),
            DirectCallersCount: distinctSites.Count(site => site.Depth == 1),
            TransitiveImpactCount: distinctSites.Count,
            MaxDepthReached: maxDepthReached,
            CallSites: shownSites,
            AffectedProjects: affectedProjects,
            AffectedFiles: affectedFiles,
            IsTruncated: isTruncated,
            RequestedDepth: maxDepth,
            EffectiveDepth: depth,
            VisitedSymbolCount: totalNodesExplored,
            IsTruncatedByNodeLimit: truncatedByNodeLimit,
            IsDepthClamped: maxDepth != depth,
            EffectiveNodeLimit: effectiveNodeLimit,
            TransitiveCallSitesCount: distinctSites.Count(site => site.Depth > 1));
    }

    private static string GetStableSymbolId(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction)
        {
            var container = localFunction.ContainingSymbol;
            while (container is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
            {
                container = container.ContainingSymbol;
            }

            var location = localFunction.Locations.FirstOrDefault(candidate => candidate.IsInSource);
            if (location is not null)
            {
                var start = location.GetLineSpan().StartLinePosition;
                return $"{GetStableSymbolId(container!)}#lf:{localFunction.Name}@{start.Line + 1}:{start.Character + 1}";
            }
        }

        return DocumentationCommentId.CreateDeclarationId(symbol)
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
