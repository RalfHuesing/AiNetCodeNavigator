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
    public const int MaxNodes = 100;

    public static async Task<SymbolImpactPayload> AnalyzeSymbolImpactAsync(
        ISymbol symbol,
        Solution solution,
        int maxDepth = 3,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var depth = Math.Clamp(maxDepth, 1, MaxAllowedDepth);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
        var queue = new Queue<(ISymbol Symbol, int Level)>();
        queue.Enqueue((symbol, 1));

        var allSites = new List<ImpactCallSiteEntry>();
        var directCallersCount = 0;
        var maxDepthReached = 0;
        var totalNodesExplored = 0;

        while (queue.Count > 0 && totalNodesExplored < MaxNodes)
        {
            ct.ThrowIfCancellationRequested();
            var (currentSymbol, currentLevel) = queue.Dequeue();
            totalNodesExplored++;

            if (currentLevel > maxDepthReached)
            {
                maxDepthReached = currentLevel;
            }

            var references = await SymbolFinder.FindReferencesAsync(currentSymbol, solution, ct).ConfigureAwait(false);

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

                    if (SymbolEqualityComparer.Default.Equals(caller, currentSymbol)) continue;

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

                    if (currentLevel == 1)
                    {
                        directCallersCount++;
                    }

                    allSites.Add(new ImpactCallSiteEntry(
                        FilePath: relPath,
                        Line: line,
                        CallingMember: callerName,
                        CallingMemberHandoffId: handoff,
                        ProjectName: doc.Project.Name,
                        Depth: currentLevel));

                    if (currentLevel < depth && visited.Add(caller))
                    {
                        queue.Enqueue((caller, currentLevel + 1));
                    }
                }
            }
        }

        var distinctSites = allSites
            .GroupBy(s => (s.FilePath, s.Line, s.CallingMember))
            .Select(g => g.OrderBy(s => s.Depth).First())
            .OrderBy(s => s.Depth)
            .ThenBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Line)
            .ToList();

        var isTruncated = distinctSites.Count > maxResults;
        var shownSites = distinctSites.Take(maxResults).ToList();

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
            DirectCallersCount: directCallersCount,
            TransitiveImpactCount: distinctSites.Count,
            MaxDepthReached: maxDepthReached,
            CallSites: shownSites,
            AffectedProjects: affectedProjects,
            AffectedFiles: affectedFiles,
            IsTruncated: isTruncated);
    }
}
