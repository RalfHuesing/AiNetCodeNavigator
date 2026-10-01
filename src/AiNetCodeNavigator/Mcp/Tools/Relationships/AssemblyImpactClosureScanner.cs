using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.Mcp.Formatting;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal sealed record AssemblyImpactClosureScanResult(
    SymbolImpactPayload? Impact,
    ResultError? Error,
    string? ErrorField,
    bool IsTruncated,
    string? NextAction);

internal static class AssemblyImpactClosureScanner
{
    private const int MaxAssemblies = 32;

    internal static async Task<AssemblyImpactClosureScanResult> ScanAsync(
        string targetPath, string identifier, int depth, int maxResults, CancellationToken ct)
    {
        var rootResult = await AssemblyNavigationSessionScope.OpenAsync(targetPath, ct).ConfigureAwait(false);
        if (!rootResult.IsSuccess)
            return Failure(rootResult.Error!.Value, "$.targetPath");
        await using var root = rootResult.Value!;
        var handoffResult = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, ct).ConfigureAwait(false);
        if (!handoffResult.IsSuccess)
            return Failure(handoffResult.Error!.Value, "$.symbolIdentifier");
        await using var handoff = handoffResult.Value!;

        var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
        var ownerPath = Path.GetFullPath(handoff.Origin.CanonicalPath);
        var references = root.Context.References
            .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .Select(reference => (Reference: reference, Path: Path.GetFullPath(reference.ResolvedPath!)))
            .ToArray();
        if (!string.Equals(rootPath, ownerPath, StringComparison.OrdinalIgnoreCase)
            && !references.Any(item => string.Equals(item.Path, ownerPath, StringComparison.OrdinalIgnoreCase)))
            return Failure(new ResultError(NavigationErrorCodes.TargetMismatch,
                "The symbol handoff is not owned by the selected assembly or its current reference snapshot.",
                "Use a handoff returned by this assembly's find_symbol(includeReferences=true) result."), "$.symbolIdentifier");

        var declarationId = DocumentationCommentId.CreateDeclarationId(handoff.Symbol);
        if (string.IsNullOrWhiteSpace(declarationId) || handoff.Symbol.ContainingAssembly is null)
            return Failure(new ResultError(NavigationErrorCodes.InvalidArgument,
                "The selected handoff has no stable assembly declaration identity.",
                "Use a declaration returned by find_symbol(includeReferences=true)."), "$.symbolIdentifier");

        var paths = new List<string> { rootPath };
        foreach (var item in references.OrderBy(item => item.Reference.Depth).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsFrameworkPath(item.Path) || paths.Contains(item.Path, StringComparer.OrdinalIgnoreCase)) continue;
            paths.Add(item.Path);
        }
        var ownerLimit = paths.Count > MaxAssemblies;
        if (ownerLimit)
        {
            paths = paths.Take(MaxAssemblies).ToList();
            if (!paths.Contains(ownerPath, StringComparer.OrdinalIgnoreCase))
            {
                paths[^1] = ownerPath;
                paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        var owners = new List<(string Path, SymbolImpactPayload Impact)>();
        var scopes = new List<AssemblyNavigationSessionScope>();
        var failed = false;
        try
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                AssemblyNavigationSessionScope scope;
                if (string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase)) scope = root;
                else
                {
                    var opened = await AssemblyNavigationSessionScope.OpenAsync(path, ct).ConfigureAwait(false);
                    if (!opened.IsSuccess) { failed = true; continue; }
                    scope = opened.Value!;
                    scopes.Add(scope);
                }

                var symbol = string.Equals(path, ownerPath, StringComparison.OrdinalIgnoreCase)
                    ? ResolveOwnedSourceSymbol(declarationId, scope)
                    : ResolveMetadataSymbol(declarationId, handoff.Identity, scope.Context.Compilation);
                if (symbol is null) continue;
                if (scope.Context.Status is not AssemblySessionStatus.Complete) failed = true;
                try
                {
                    var impact = await ImpactAnalyzer.AnalyzeSymbolImpactAsync(symbol, scope.Solution,
                        Math.Clamp(depth, 1, 3), Math.Max(maxResults, 1), ct,
                        handoffFormatter: CreateInternalFormatter(scope)).ConfigureAwait(false);
                    owners.Add((path, impact));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    failed = true;
                }
            }

            var allSites = owners.SelectMany(owner => owner.Impact.CallSites.Select(site => site with { OwnerTargetPath = owner.Path }))
                .GroupBy(site => (site.OwnerTargetPath, site.FilePath, site.Line, site.Column, site.CallingMember,
                    site.CallingMemberHandoffId, site.Depth, site.ReachedFromSymbolId, site.ReachedFromSymbolHandoffId))
                .Select(group => group.First())
                .OrderBy(site => site.Depth)
                .ThenBy(site => site.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.FilePath, StringComparer.Ordinal)
                .ThenBy(site => site.Line)
                .ThenBy(site => site.Column)
                .ToArray();
            var visible = allSites.Take(Math.Max(maxResults, 1)).Select(site => site with
            {
                CallingMemberHandoffId = Externalize(site.CallingMemberHandoffId),
                ReachedFromSymbolHandoffId = Externalize(site.ReachedFromSymbolHandoffId),
            }).ToArray();
            var nodeLimited = owners.Any(owner => owner.Impact.IsTruncatedByNodeLimit);
            var depthClamped = owners.Any(owner => owner.Impact.IsDepthClamped);
            var crossOwnerDepth = depth > 1;
            var unresolved = root.Context.References.Any(reference => !reference.Resolved);
            var truncated = allSites.Length > maxResults || nodeLimited || depthClamped || ownerLimit || failed || unresolved
                || crossOwnerDepth || owners.Any(owner => !owner.Impact.IsComplete);
            var impactPayload = new SymbolImpactPayload(
                handoff.Symbol.Name,
                handoff.Symbol.Kind.ToString().ToLowerInvariant(),
                owners.Sum(owner => owner.Impact.DirectCallersCount),
                owners.Sum(owner => owner.Impact.TransitiveImpactCount),
                owners.Select(owner => owner.Impact.MaxDepthReached).DefaultIfEmpty(0).Max(),
                visible,
                owners.SelectMany(owner => owner.Impact.AffectedProjects).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                owners.SelectMany(owner => owner.Impact.AffectedFiles).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                truncated,
                depth,
                owners.Select(owner => owner.Impact.EffectiveDepth).DefaultIfEmpty(Math.Clamp(depth, 1, 3)).Min(),
                owners.Sum(owner => owner.Impact.VisitedSymbolCount),
                nodeLimited,
                depthClamped,
                owners.Select(owner => owner.Impact.EffectiveNodeLimit).DefaultIfEmpty(ImpactAnalyzer.MaxNodes).Min(),
                owners.Sum(owner => owner.Impact.TransitiveCallSitesCount));
            var nextAction = crossOwnerDepth
                ? "Cross-assembly impact expansion beyond each owner solution is not composed yet; this result is incomplete."
                : failed || ownerLimit || unresolved
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : "Increase maxResults or reduce the graph scope and repeat the query.";
            return new(impactPayload, null, null, truncated, nextAction);
        }
        finally
        {
            foreach (var scope in scopes) await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ISymbol? ResolveOwnedSourceSymbol(string declarationId, AssemblyNavigationSessionScope scope)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, scope.Context.Compilation)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, scope.Context.Assembly)
                && HasSourceDeclaration(symbol, scope.Solution, scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static ISymbol? ResolveMetadataSymbol(string declarationId, AssemblyIdentityDto expected, Compilation compilation)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, compilation)
            .Where(symbol => symbol.ContainingAssembly is { } assembly && IdentityMatches(assembly.Identity, expected))
            .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static Func<ISymbol, string?> CreateInternalFormatter(AssemblyNavigationSessionScope scope)
    {
        var identity = AnalysisSymbolIdentity.ForAssembly(scope.Context.Origin.CanonicalPath, scope.Context.Origin.ContentHash,
            scope.Context.Generation, scope.Context.ReferenceSnapshotHash);
        return symbol =>
        {
            if (!HasSourceDeclaration(symbol, scope.Solution, scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot)) return null;
            var id = DocumentationCommentId.CreateDeclarationId(symbol);
            if (string.IsNullOrWhiteSpace(id)) return null;
            var matches = DocumentationCommentId.GetSymbolsForDeclarationId(id, scope.Context.Compilation)
                .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, scope.Context.Assembly))
                .Distinct(SymbolEqualityComparer.Default).Take(2).ToArray();
            return matches.Length == 1 ? identity.FormatHandoff(matches[0]) : null;
        };
    }

    private static bool HasSourceDeclaration(ISymbol symbol, Solution solution, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return false;
        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return symbol.Locations.Any(location => location.IsInSource && location.SourceTree is { FilePath.Length: > 0 } tree
            && Path.GetFullPath(tree.FilePath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && solution.GetDocument(tree) is not null);
    }

    private static bool IsFrameworkPath(string path) => path.Contains("\\shared\\Microsoft.NETCore.App\\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("\\packs\\Microsoft.NETCore.App.Ref\\", StringComparison.OrdinalIgnoreCase);

    private static bool IdentityMatches(AssemblyIdentity actual, AssemblyIdentityDto expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.Version?.ToString(), expected.Version, StringComparison.Ordinal)
        && string.Equals(string.IsNullOrWhiteSpace(actual.CultureName) ? "neutral" : actual.CultureName,
            string.IsNullOrWhiteSpace(expected.Culture) ? "neutral" : expected.Culture, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Convert.ToHexString(actual.PublicKeyToken.ToArray()), expected.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    private static string? Externalize(string? internalHandoff) => string.IsNullOrWhiteSpace(internalHandoff)
        ? null
        : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoff);

    private static AssemblyImpactClosureScanResult Failure(ResultError error, string field) => new(null, error, field, false, null);
}
