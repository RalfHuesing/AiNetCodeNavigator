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
/// Calculates the transitive blast radius (impact) of changes to a C# symbol.
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
        int maxNodes = MaxNodes,
        Func<ISymbol, string?>? handoffFormatter = null)
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

        var allSites = new List<(ImpactCallSiteEntry Entry, string CallerProjectPath, string CallerProjectId, string CallerSymbolId)>();
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
            var reachedFromSymbolId = RelationshipSymbolIdentity.GetStableId(currentSymbol);
            var reachedFromSymbolHandoffId = handoffFormatter is null
                ? SourceHandoffFormatter.Format(currentSymbol, solution, handoffIdentity)
                : handoffFormatter(currentSymbol);

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
                    var column = lineSpan.StartLinePosition.Character + 1;

                    var callerName = caller switch
                    {
                        IMethodSymbol m => $"{m.ContainingType?.Name}.{m.Name}",
                        IPropertySymbol p => $"{p.ContainingType?.Name}.{p.Name}",
                        _ => caller.Name
                    };

                    var handoff = handoffFormatter is null
                        ? SourceHandoffFormatter.Format(caller, solution, handoffIdentity)
                        : handoffFormatter(caller);

                    var entry = new ImpactCallSiteEntry(
                        FilePath: relPath,
                        Line: line,
                        Column: column,
                        CallingMember: callerName,
                        CallingMemberHandoffId: handoff,
                        ProjectName: doc.Project.Name,
                        Depth: currentLevel,
                        ReachedFromSymbolId: reachedFromSymbolId,
                        ReachedFromSymbolHandoffId: reachedFromSymbolHandoffId);
                    var callerProjectPath = doc.Project.FilePath is { Length: > 0 } projectPath
                        ? Path.GetFullPath(projectPath)
                        : string.Empty;
                    var callerSymbolId = RelationshipSymbolIdentity.GetStableId(caller);
                    allSites.Add((entry, callerProjectPath, doc.Project.Id.Id.ToString("N"), callerSymbolId));

                    if (currentLevel < depth && visited.Add(caller))
                    {
                        queue.Enqueue((caller, currentLevel + 1));
                    }
                }
            }
        }

        var distinctSites = allSites
            .GroupBy(site => (site.Entry, site.CallerProjectId, site.CallerSymbolId))
            .Select(group => group.First())
            .OrderBy(site => site.Entry.Depth)
            .ThenBy(site => site.CallerProjectPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.CallerProjectPath, StringComparer.Ordinal)
            .ThenBy(site => site.Entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Entry.FilePath, StringComparer.Ordinal)
            .ThenBy(site => site.Entry.Line)
            .ThenBy(site => site.Entry.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.Entry.CallingMember, StringComparer.Ordinal)
            .ThenBy(site => site.CallerProjectId, StringComparer.Ordinal)
            .ThenBy(site => site.CallerSymbolId, StringComparer.Ordinal)
            .ThenBy(site => site.Entry.ReachedFromSymbolId, StringComparer.Ordinal)
            .ThenBy(site => site.Entry.ReachedFromSymbolHandoffId, StringComparer.Ordinal)
            .ThenBy(site => site.Entry.Column)
            .ToList();

        var isTruncated = distinctSites.Count > effectiveResultLimit;
        var truncatedByNodeLimit = queue.Count > 0 && totalNodesExplored >= effectiveNodeLimit;
        var shownSites = distinctSites.Take(effectiveResultLimit).Select(site => site.Entry).ToList();

        var affectedProjects = distinctSites
            .Select(s => s.Entry.ProjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var affectedFiles = distinctSites
            .Select(s => s.Entry.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SymbolImpactPayload(
            TargetSymbol: symbol.Name,
            TargetKind: symbol.Kind.ToString().ToLowerInvariant(),
            DirectCallersCount: distinctSites.Count(site => site.Entry.Depth == 1),
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
            TransitiveCallSitesCount: distinctSites.Count(site => site.Entry.Depth > 1));
    }

}
