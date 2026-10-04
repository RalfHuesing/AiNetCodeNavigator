using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal sealed record AssemblyReferencesClosureScanResult(
    FindReferencesResult? References,
    ResultError? Error,
    string? ErrorField,
    bool IsTruncated,
    string? NextAction,
    AnalysisSymbolIdentity? AnalysisIdentity = null,
    IReadOnlyList<string>? OmissionReasons = null);

internal static class AssemblyReferencesClosureScanner
{
    private const int MaxVisitedSymbols = 200;

    internal static async Task<AssemblyReferencesClosureScanResult> ScanAsync(
        string targetPath,
        string identifier,
        int maxResults,
        int depth,
        SymbolScopeType scopeType,
        bool includeGenerated,
        CancellationToken ct,
        bool externalizeHandoffs = true,
        bool includeSummary = false)
    {
        var opened = await AssemblyReferenceClosureSession.OpenAsync(targetPath, identifier, ct).ConfigureAwait(false);
        if (opened.Error is { } error) return Failure(error, opened.ErrorField!);
        await using var session = opened.Session!;

        var requestedDepth = Math.Clamp(depth, 1, 3);
        var queue = new Queue<Frontier>();
        var visited = new HashSet<FrontierKey>(FrontierKeyComparer.Instance);
        var seed = new Frontier(session.HandoffOwnerPath, session.HandoffIdentity,
            session.DeclarationCommentId, 0);
        queue.Enqueue(seed);
        visited.Add(new(seed.OwnerPath, seed.DeclarationId));

        var locations = new List<ReferenceLocationEntry>();
        var locationKeys = new HashSet<ReferenceLocationKey>(ReferenceLocationKeyComparer.Instance);
        var nodeLimited = false;
        var traversalLimited = false;
        var depthClamped = false;
        var effectiveNodeLimit = FindReferencesResolver.DefaultMaxVisitedSymbols;
        var hiddenLocalResults = 0;
        while (queue.TryDequeue(out var frontier))
        {
            ct.ThrowIfCancellationRequested();
            if (frontier.Depth >= requestedDepth) continue;
            foreach (var owner in session.Owners)
            {
                ct.ThrowIfCancellationRequested();
                var targetSymbol = session.ResolveDeclaration(owner, frontier.OwnerPath,
                    frontier.DeclarationId, frontier.Identity);
                if (targetSymbol is null)
                {
                    if (string.Equals(owner.TargetPath, frontier.OwnerPath, StringComparison.OrdinalIgnoreCase))
                        traversalLimited = true;
                    continue;
                }

                FindReferencesResult scan;
                try
                {
                    scan = await FindReferencesResolver.FindReferencesAsync(targetSymbol, owner.Scope.Solution,
                        int.MaxValue, 1, ct, scope: scopeType, includeGenerated: includeGenerated,
                        handoffFormatter: session.CreateInternalFormatter(owner), ownerTargetPath: owner.TargetPath).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    traversalLimited = true;
                    continue;
                }

                nodeLimited |= scan.IsTruncatedByNodeLimit;
                depthClamped |= scan.IsDepthClamped;
                effectiveNodeLimit = Math.Min(effectiveNodeLimit, scan.EffectiveNodeLimit);
                traversalLimited |= scan.IsTruncated || scan.IsTruncatedByNodeLimit || scan.IsDepthClamped;
                hiddenLocalResults += Math.Max(0, scan.TotalCount - scan.References.Count);
                var nextDepth = frontier.Depth + 1;
                foreach (var reference in scan.References)
                {
                    var entry = reference with { Depth = nextDepth, OwnerTargetPath = owner.TargetPath };
                    if (locationKeys.Add(new(entry.OwnerTargetPath ?? owner.TargetPath, entry.FilePath,
                        entry.Line, entry.Column, entry.EnclosingSymbolHandoffId, frontier.OwnerPath,
                        entry.ReachedFromSymbolId, nextDepth)))
                        locations.Add(entry);

                    if (nextDepth >= requestedDepth) continue;
                    var caller = session.ResolveInternalSourceHandoff(entry.EnclosingSymbolHandoffId, entry.OwnerTargetPath);
                    if (caller is null)
                    {
                        traversalLimited = true;
                        continue;
                    }
                    var key = new FrontierKey(caller.Owner.TargetPath, caller.DocumentationCommentId);
                    if (visited.Contains(key)) continue;
                    if (visited.Count >= MaxVisitedSymbols)
                    {
                        nodeLimited = true;
                        traversalLimited = true;
                        continue;
                    }
                    visited.Add(key);
                    if (caller.Owner.Scope.Context.Identity is not { } callerIdentity)
                    {
                        traversalLimited = true;
                        continue;
                    }
                    queue.Enqueue(new(caller.Owner.TargetPath, callerIdentity,
                        caller.DocumentationCommentId, nextDepth));
                }
            }
        }

        var ordered = locations
            .OrderBy(entry => entry.Depth)
            .ThenBy(entry => entry.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FilePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Column)
            .ThenBy(entry => entry.EnclosingSymbolName, StringComparer.Ordinal)
            .ToArray();
        var page = ordered.Take(Math.Max(maxResults, 1));
        var selected = (externalizeHandoffs
            ? page.Select(entry => entry with
            {
                EnclosingSymbolHandoffId = AssemblyReferenceClosureSession.Externalize(entry.EnclosingSymbolHandoffId),
                ReachedFromSymbolHandoffId = AssemblyReferenceClosureSession.Externalize(entry.ReachedFromSymbolHandoffId),
            })
            : page).ToArray();

        var totalCount = ordered.Length + hiddenLocalResults;
        var ownerLimit = session.OwnerLimitReached || session.HasFailedOwners;
        var unresolved = session.HasUnresolvedReferences;
        var truncated = totalCount > maxResults || nodeLimited || depthClamped || traversalLimited || ownerLimit || unresolved;
        var result = new FindReferencesResult(
            session.HandoffSymbol.Name,
            session.HandoffSymbol.Kind.ToString().ToLowerInvariant(),
            selected,
            totalCount,
            truncated,
            requestedDepth,
            requestedDepth,
            visited.Count,
            nodeLimited,
            depthClamped,
            effectiveNodeLimit);
        var nextAction = !truncated ? null
            : ownerLimit || unresolved
                ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                : nodeLimited || depthClamped || traversalLimited
                    ? "Cross-assembly traversal reached an owner, result, or graph bound before completing the requested depth."
                    : "Increase maxResults and repeat the query.";
        var omissions = new List<string>();
        if (totalCount > maxResults) omissions.Add("maxResults");
        if (nodeLimited) omissions.Add("nodeLimit");
        if (depthClamped) omissions.Add("depthLimit");
        if (traversalLimited) omissions.Add("traversalLimit");
        if (ownerLimit) omissions.Add("referenceOwnerLimit");
        if (unresolved) omissions.Add("unresolvedReferences");
        if (includeSummary) result = result with { Summary = ReferenceSummary.Create(ordered, omissions.Where(reason => reason != "maxResults").ToArray()) };
        return new(result, null, null, truncated, nextAction, session.RootAnalysisIdentity, omissions);
    }

    private static AssemblyReferencesClosureScanResult Failure(ResultError error, string field) => new(null, error, field, false, null);

    private readonly record struct Frontier(string OwnerPath, AssemblyIdentityDto Identity, string DeclarationId, int Depth);
    private readonly record struct FrontierKey(string OwnerPath, string DeclarationId);
    private readonly record struct ReferenceLocationKey(string OwnerPath, string FilePath, int Line, int Column,
        string? CallerIdentity, string ReachedOwnerPath, string ReachedFromSymbolId, int Depth);

    private sealed class FrontierKeyComparer : IEqualityComparer<FrontierKey>
    {
        internal static FrontierKeyComparer Instance { get; } = new();
        public bool Equals(FrontierKey x, FrontierKey y) =>
            string.Equals(x.OwnerPath, y.OwnerPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.DeclarationId, y.DeclarationId, StringComparison.Ordinal);
        public int GetHashCode(FrontierKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.OwnerPath),
            StringComparer.Ordinal.GetHashCode(value.DeclarationId));
    }

    private sealed class ReferenceLocationKeyComparer : IEqualityComparer<ReferenceLocationKey>
    {
        internal static ReferenceLocationKeyComparer Instance { get; } = new();
        public bool Equals(ReferenceLocationKey x, ReferenceLocationKey y) =>
            string.Equals(x.OwnerPath, y.OwnerPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase)
            && x.Line == y.Line && x.Column == y.Column && x.Depth == y.Depth
            && string.Equals(x.CallerIdentity, y.CallerIdentity, StringComparison.Ordinal)
            && string.Equals(x.ReachedOwnerPath, y.ReachedOwnerPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ReachedFromSymbolId, y.ReachedFromSymbolId, StringComparison.Ordinal);
        public int GetHashCode(ReferenceLocationKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.OwnerPath),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.FilePath), value.Line, value.Column, value.Depth,
            value.CallerIdentity is null ? 0 : StringComparer.Ordinal.GetHashCode(value.CallerIdentity),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.ReachedOwnerPath),
            StringComparer.Ordinal.GetHashCode(value.ReachedFromSymbolId));
    }
}
