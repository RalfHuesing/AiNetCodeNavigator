using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetCodeNavigator.Core.Assemblies;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Mcp.Tools.Relationships;

internal sealed record AssemblyReferencesClosureScanResult(
    FindReferencesResult? References,
    ResultError? Error,
    string? ErrorField,
    bool IsTruncated,
    string? NextAction);

internal static class AssemblyReferencesClosureScanner
{
    private const int MaxAssemblies = 32;

    internal static async Task<AssemblyReferencesClosureScanResult> ScanAsync(
        string targetPath, string identifier, int maxResults, int depth, SymbolScopeType scopeType,
        bool includeGenerated, CancellationToken ct)
    {
        var rootResult = await AssemblyNavigationSessionScope.OpenAsync(targetPath, ct).ConfigureAwait(false);
        if (!rootResult.IsSuccess) return Failure(rootResult.Error!.Value, "$.targetPath");
        await using var root = rootResult.Value!;
        var handoffResult = await AssemblySymbolHandoffResolver.ResolveAsync(identifier, ct).ConfigureAwait(false);
        if (!handoffResult.IsSuccess) return Failure(handoffResult.Error!.Value, "$.symbolIdentifier");
        await using var handoff = handoffResult.Value!;

        var rootPath = Path.GetFullPath(root.Context.Origin.CanonicalPath);
        var ownerPath = Path.GetFullPath(handoff.Origin.CanonicalPath);
        var references = root.Context.References
            .Where(reference => reference.Resolved && !string.IsNullOrWhiteSpace(reference.ResolvedPath))
            .Select(reference => (Reference: reference, Path: Path.GetFullPath(reference.ResolvedPath!))).ToArray();
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

        var scans = new List<(string Path, AssemblyNavigationSessionScope Scope, FindReferencesResult References)>();
        var scopes = new List<AssemblyNavigationSessionScope>();
        var failedOwners = new List<string>();
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
                    if (!opened.IsSuccess) { failedOwners.Add(path); continue; }
                    scope = opened.Value!;
                    scopes.Add(scope);
                }
                var symbol = string.Equals(path, ownerPath, StringComparison.OrdinalIgnoreCase)
                    ? ResolveOwnedSourceSymbol(declarationId, scope)
                    : ResolveMetadataSymbol(declarationId, handoff.Identity, scope.Context.Compilation);
                if (symbol is null) continue;
                if (scope.Context.Status is not AssemblySessionStatus.Complete) failedOwners.Add(path);
                try
                {
                    var result = await FindReferencesResolver.FindReferencesAsync(symbol, scope.Solution,
                        Math.Max(maxResults, 1), depth, ct, scope: scopeType, includeGenerated: includeGenerated,
                        handoffFormatter: CreateInternalFormatter(scope), ownerTargetPath: path).ConfigureAwait(false);
                    scans.Add((path, scope, result));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    failedOwners.Add(path);
                }
            }

            var candidates = scans.SelectMany(scan => scan.References.References)
                .OrderBy(entry => entry.Depth)
                .ThenBy(entry => entry.OwnerTargetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FilePath, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.EnclosingSymbolName, StringComparer.Ordinal)
                .ThenBy(entry => entry.ReachedFromSymbolName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Column)
                .ToArray();
            var selected = candidates.Take(Math.Max(maxResults, 1)).ToArray();
            var visible = selected.Select(entry => entry with
            {
                EnclosingSymbolHandoffId = Externalize(entry.EnclosingSymbolHandoffId),
                ReachedFromSymbolHandoffId = Externalize(entry.ReachedFromSymbolHandoffId),
            }).ToArray();
            var totalCount = scans.Sum(scan => scan.References.TotalCount);
            var nodeLimit = scans.Any(scan => scan.References.IsTruncatedByNodeLimit);
            var depthClamped = scans.Any(scan => scan.References.IsDepthClamped);
            var crossOwnerDepth = depth > 1;
            var unresolved = root.Context.References.Any(reference => !reference.Resolved);
            var truncated = crossOwnerDepth || ownerLimit || failedOwners.Count > 0 || unresolved
                || scans.Any(scan => !scan.References.IsComplete) || totalCount > maxResults;
            var resultPayload = new FindReferencesResult(handoff.Symbol.Name, handoff.Symbol.Kind.ToString().ToLowerInvariant(),
                visible, totalCount, truncated, depth,
                scans.Select(scan => scan.References.EffectiveDepth).DefaultIfEmpty(depth).Min(),
                scans.Sum(scan => scan.References.VisitedSymbolCount), nodeLimit, depthClamped,
                scans.Select(scan => scan.References.EffectiveNodeLimit).DefaultIfEmpty(FindReferencesResolver.DefaultMaxVisitedSymbols).Min());
            var nextAction = crossOwnerDepth
                ? "Cross-assembly caller chaining beyond each owner solution is not composed yet; this result covers direct owner scans and is incomplete."
                : failedOwners.Count > 0 || ownerLimit || unresolved
                    ? "The bounded reference-source closure is incomplete; inspect unresolved or unsupported references, then repeat the query."
                    : "Increase maxResults or depth and repeat the query.";
            return new(resultPayload, null, null, truncated, nextAction);
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

    private static string? Externalize(string? internalId) => string.IsNullOrWhiteSpace(internalId)
        ? null
        : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);

    private static AssemblyReferencesClosureScanResult Failure(ResultError error, string field) => new(null, error, field, false, null);
}
