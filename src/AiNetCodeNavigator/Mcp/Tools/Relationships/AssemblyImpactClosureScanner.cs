using System.Collections.Generic;
using System.Linq;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal sealed record AssemblyImpactClosureScanResult(
    SymbolImpactPayload? Impact,
    ResultError? Error,
    string? ErrorField,
    bool IsTruncated,
    string? NextAction,
    AnalysisSymbolIdentity? AnalysisIdentity = null,
    IReadOnlyList<string>? OmissionReasons = null);

internal static class AssemblyImpactClosureScanner
{
    private const int ClosureProjectionLimit = 1_000;

    internal static async Task<AssemblyImpactClosureScanResult> ScanAsync(
        string targetPath, string identifier, int depth, int maxResults, CancellationToken ct)
    {
        // Impact and references describe the same caller graph. Reuse the bounded owner traversal so
        // depth, owner paths, and handoffs have identical cross-assembly semantics.
        var closure = await AssemblyReferencesClosureScanner.ScanAsync(targetPath, identifier,
            ClosureProjectionLimit, depth, SymbolScopeType.All, includeGenerated: false, ct,
            externalizeHandoffs: false).ConfigureAwait(false);
        if (closure.Error is { } error)
            return new(null, error, closure.ErrorField, false, null, closure.AnalysisIdentity, closure.OmissionReasons);

        var references = closure.References!;
        var allSites = references.References.Select(reference => new ImpactCallSiteEntry(
                FilePath: reference.FilePath,
                Line: reference.Line,
                Column: reference.Column,
                CallingMember: reference.EnclosingSymbolName,
                CallingMemberHandoffId: reference.EnclosingSymbolHandoffId,
                ProjectName: reference.ProjectName,
                Depth: reference.Depth,
                ReachedFromSymbolId: reference.ReachedFromSymbolId,
                ReachedFromSymbolHandoffId: reference.ReachedFromSymbolHandoffId,
                OwnerTargetPath: reference.OwnerTargetPath,
                EvidenceKind: reference.EvidenceKind))
            .OrderBy(site => site.Depth)
            .ThenBy(site => site.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.FilePath, StringComparer.Ordinal)
            .ThenBy(site => site.Line)
            .ThenBy(site => site.Column)
            .ToArray();
        var selected = allSites.Take(Math.Max(maxResults, 1)).Select(site => site with
        {
            CallingMemberHandoffId = AssemblyReferenceClosureSession.Externalize(site.CallingMemberHandoffId),
            ReachedFromSymbolHandoffId = AssemblyReferenceClosureSession.Externalize(site.ReachedFromSymbolHandoffId),
        }).ToArray();
        var truncated = closure.IsTruncated || references.IsTruncated || references.TotalCount > maxResults;
        var payload = new SymbolImpactPayload(
            references.TargetSymbolName,
            references.TargetKind,
            closure.DirectCount,
            references.TotalCount,
            closure.MaxDepthReached,
            selected,
            (closure.AffectedProjects ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            (closure.AffectedFiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            truncated,
            references.RequestedDepth,
            references.EffectiveDepth,
            references.VisitedSymbolCount,
            references.IsTruncatedByNodeLimit,
            references.IsDepthClamped,
            references.EffectiveNodeLimit,
            Math.Max(0, references.TotalCount - closure.DirectCount));
        var nextAction = closure.NextAction ?? (truncated
            ? "Increase maxResults and repeat the query."
            : null);
        var omissions = new List<string>(closure.OmissionReasons ?? []);
        if (references.TotalCount > maxResults && !omissions.Contains("maxResults", StringComparer.Ordinal)) omissions.Add("maxResults");
        return new(payload, null, null, truncated, nextAction, closure.AnalysisIdentity, omissions);
    }
}
