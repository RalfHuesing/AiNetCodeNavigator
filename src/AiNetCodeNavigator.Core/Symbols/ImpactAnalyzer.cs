#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Projects the shared bounded caller traversal into impact-specific totals and summaries.</summary>
public static class ImpactAnalyzer
{
    public const int MaxAllowedDepth = FindReferencesResolver.MaxReferenceDepth;
    public const int MaxNodes = FindReferencesResolver.DefaultMaxVisitedSymbols;

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
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);

        var references = await FindReferencesResolver.FindReferencesAsync(
            symbol,
            solution,
            maxResults: int.MaxValue,
            depth: maxDepth,
            ct: ct,
            maxNodes: maxNodes,
            handoffFormatter: handoffFormatter).ConfigureAwait(false);
        var shown = references.References.Take(Math.Max(maxResults, 1)).Select(site => new ImpactCallSiteEntry(
            FilePath: site.FilePath,
            Line: site.Line,
            Column: site.Column,
            CallingMember: site.EnclosingSymbolName,
            CallingMemberHandoffId: site.EnclosingSymbolHandoffId,
            ProjectName: site.ProjectName,
            Depth: site.Depth,
            ReachedFromSymbolId: site.ReachedFromSymbolId,
            ReachedFromSymbolHandoffId: site.ReachedFromSymbolHandoffId,
            EvidenceKind: site.EvidenceKind)).ToArray();
        var sites = references.References;
        var normalizedDepth = Math.Clamp(maxDepth, 1, MaxAllowedDepth);

        return new SymbolImpactPayload(
            TargetSymbol: symbol.Name,
            TargetKind: symbol.Kind.ToString().ToLowerInvariant(),
            DirectCallersCount: sites.Count(site => site.Depth == 1),
            TransitiveImpactCount: references.TotalCount,
            MaxDepthReached: sites.Count == 0 ? 1 : sites.Max(site => site.Depth),
            CallSites: shown,
            AffectedProjects: sites.Select(site => site.ProjectName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            AffectedFiles: sites.Select(site => site.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            IsTruncated: references.TotalCount > shown.Length,
            RequestedDepth: maxDepth,
            EffectiveDepth: normalizedDepth,
            VisitedSymbolCount: references.VisitedSymbolCount,
            IsTruncatedByNodeLimit: references.IsTruncatedByNodeLimit,
            IsDepthClamped: references.IsDepthClamped,
            EffectiveNodeLimit: references.EffectiveNodeLimit,
            TransitiveCallSitesCount: sites.Count(site => site.Depth > 1));
    }
}
