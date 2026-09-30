#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Such-Engine für C#-Symbole via Roslyn Solution und SymbolFinder.
/// Filtert nach Name, Pattern, SymbolKind und ScopeType (Production/Tests).
/// </summary>
public static class FindSymbolScanner
{
    public static async Task<FindSymbolScanResult> FindMatchesWithDetailsAsync(
        FindSymbolScanRequest request,
        CancellationToken ct = default)
    {
        var currentSourceIdentity = await AnalysisSymbolIdentity.ForSourceAsync(request.Solution, ct).ConfigureAwait(false);
        if (request.SourceIdentity is not null
            && (currentSourceIdentity is null || !request.SourceIdentity.Matches(currentSourceIdentity)))
        {
            var sameTarget = currentSourceIdentity is not null
                && !request.SourceIdentity.IsAssembly
                && SymbolHandoffToken.TryCreateTarget(request.SourceIdentity.CanonicalPath, out var suppliedTarget)
                && SymbolHandoffToken.TryCreateTarget(request.Solution.FilePath ?? string.Empty, out var actualTarget)
                && string.Equals(suppliedTarget, actualTarget, StringComparison.Ordinal);
            var sameContent = currentSourceIdentity is not null
                && string.Equals(request.SourceIdentity.ContentHash, currentSourceIdentity.ContentHash, StringComparison.OrdinalIgnoreCase);
            var code = sameTarget && !sameContent ? NavigationErrorCodes.StaleSnapshot : NavigationErrorCodes.TargetMismatch;
            var message = code == NavigationErrorCodes.StaleSnapshot
                ? "The supplied source identity does not match the current solution snapshot."
                : "The supplied source identity does not match this target and project context.";
            return new FindSymbolScanResult(
                message,
                Array.Empty<SymbolLocationEntry>(),
                0,
                0,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new AiNetCodeNavigator.Core.Models.ResultError(code, message));
        }

        var nameFilter = SymbolNameMatcher.CreateDeclarationNameFilter(request.NamePattern);
        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            request.Solution,
            nameFilter,
            SymbolFilter.TypeAndMember,
            ct).ConfigureAwait(false);

        var nameMatches = symbols
            .Where(symbol => SymbolNameMatcher.MatchesSymbol(symbol, request.NamePattern))
            .ToList();

        var filtered = FilterByKind(nameMatches, request.Kind).ToList();
        var kindAlternatives = CreateKindAlternatives(nameMatches, request.Kind);

        if (filtered.Count == 0)
        {
            var missMessage = await FormatMissMessageAsync(request, nameMatches, ct).ConfigureAwait(false);
            return new FindSymbolScanResult(missMessage, Array.Empty<SymbolLocationEntry>(), 0, 0, false, Array.Empty<string>(), kindAlternatives);
        }

        var outputRoot = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var sourceIdentity = currentSourceIdentity;
        var allEntries = (await BuildVisibleEntriesAsync(request, filtered, outputRoot, sourceIdentity, ct).ConfigureAwait(false))
            .OrderBy(entry => GetMatchRank(entry, request.NamePattern))
            .ThenBy(entry => GetScopeRank(entry))
            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        if (allEntries.Count == 0)
        {
            return new FindSymbolScanResult(
                $"Keine Treffer für '{request.NamePattern}' im angeforderten Scope '{request.ScopeType}'.",
                Array.Empty<SymbolLocationEntry>(),
                0,
                0,
                false,
                Array.Empty<string>(),
                kindAlternatives);
        }

        var collectedEntries = allEntries.Take(Math.Max(request.MaxResults, 1)).ToList();
        var isTruncated = allEntries.Count > collectedEntries.Count;
        var text = FormatEntriesText(collectedEntries, allEntries.Count, request.MaxResults);

        return new FindSymbolScanResult(
            text,
            collectedEntries,
            allEntries.Count,
            collectedEntries.Count,
            isTruncated,
            isTruncated ? ["maxResults"] : Array.Empty<string>(),
            kindAlternatives);
    }

    private static async Task<IReadOnlyList<SymbolLocationEntry>> BuildVisibleEntriesAsync(
        FindSymbolScanRequest request,
        IReadOnlyList<ISymbol> symbols,
        string outputRoot,
        AnalysisSymbolIdentity? sourceIdentity,
        CancellationToken ct)
    {
        var grouped = GroupSymbols(symbols);
        var entries = new List<SymbolLocationEntry>(grouped.Count);

        foreach (var (symbol, declarations) in grouped)
        {
            ct.ThrowIfCancellationRequested();

            if (!MatchesScope(symbol, request.ScopeType))
            {
                continue;
            }

            var locations = CollectVisibleLocations(declarations, outputRoot);
            if (locations.Count == 0) continue;

            locations.Sort((a, b) =>
            {
                var cmp = string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : a.Line.CompareTo(b.Line);
            });

            var primaryLoc = locations[0];
            var docCommentId = symbol.GetDocumentationCommentId();
            var internalHandoffId = sourceIdentity?.FormatHandoff(symbol, request.Solution);
            var handoffId = internalHandoffId is null
                ? null
                : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoffId);

            var kindName = DescribeKind(symbol);
            var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            entries.Add(new SymbolLocationEntry(
                Name: symbol.Name,
                Kind: kindName,
                DocCommentId: docCommentId,
                HandoffId: handoffId,
                FilePath: primaryLoc.FilePath,
                Line: primaryLoc.Line,
                EndLine: primaryLoc.EndLine,
                ProjectName: primaryLoc.ProjectName,
                Signature: signature,
                Locations: locations));
        }

        return entries;
    }

    private static bool MatchesScope(ISymbol symbol, SymbolScopeType scopeType)
    {
        if (scopeType == SymbolScopeType.All) return true;
        var isTest = TestDetector.IsTestSymbol(symbol);
        return scopeType switch
        {
            SymbolScopeType.Production => !isTest,
            SymbolScopeType.Tests => isTest,
            _ => true
        };
    }

    private static List<SymbolLocationItem> CollectVisibleLocations(
        IReadOnlyList<ISymbol> declarations,
        string outputRoot)
    {
        var result = new List<SymbolLocationItem>();
        foreach (var decl in declarations)
        {
            foreach (var loc in decl.Locations)
            {
                if (!loc.IsInSource || loc.SourceTree is null) continue;
                var filePath = loc.SourceTree.FilePath;
                var relativePath = PathNormalizer.ToRelative(outputRoot, filePath);
                var lineSpan = loc.GetLineSpan();
                var startLine = lineSpan.StartLinePosition.Line + 1;
                var endLine = lineSpan.EndLinePosition.Line + 1;
                var projectName = decl.ContainingAssembly?.Name ?? string.Empty;

                result.Add(new SymbolLocationItem(relativePath, startLine, endLine, projectName));
            }
        }

        return result;
    }

    private static Dictionary<ISymbol, List<ISymbol>> GroupSymbols(IReadOnlyList<ISymbol> symbols)
    {
        var grouped = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
        foreach (var symbol in symbols)
        {
            var key = symbol.OriginalDefinition;
            if (!grouped.TryGetValue(key, out var declarations))
            {
                declarations = [];
                grouped.Add(key, declarations);
            }

            if (!declarations.Contains(symbol, SymbolEqualityComparer.Default))
            {
                declarations.Add(symbol);
            }
        }

        return grouped;
    }

    private static IEnumerable<ISymbol> FilterByKind(IEnumerable<ISymbol> symbols, SymbolKindFilter kind)
    {
        return kind switch
        {
            SymbolKindFilter.Class => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class, IsRecord: false }),
            SymbolKindFilter.Record => symbols.Where(s => s is INamedTypeSymbol { IsRecord: true }),
            SymbolKindFilter.Struct => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Struct }),
            SymbolKindFilter.Interface => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Interface }),
            SymbolKindFilter.Enum => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Enum }),
            SymbolKindFilter.Method => symbols.Where(s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary }),
            SymbolKindFilter.Property => symbols.Where(s => s is IPropertySymbol),
            SymbolKindFilter.Field => symbols.Where(s => s is IFieldSymbol),
            SymbolKindFilter.Event => symbols.Where(s => s is IEventSymbol),
            _ => symbols
        };
    }

    private static IReadOnlyList<string> CreateKindAlternatives(IReadOnlyList<ISymbol> matches, SymbolKindFilter currentKind)
    {
        if (currentKind == SymbolKindFilter.All || matches.Count == 0) return Array.Empty<string>();
        return matches
            .Select(DescribeKind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DescribeKind(ISymbol symbol) =>
        symbol switch
        {
            INamedTypeSymbol { IsRecord: true } => "record",
            INamedTypeSymbol nts => nts.TypeKind.ToString().ToLowerInvariant(),
            IMethodSymbol ms => ms.MethodKind == MethodKind.Constructor ? "constructor" : "method",
            IPropertySymbol => "property",
            IFieldSymbol => "field",
            IEventSymbol => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };

    private static int GetMatchRank(SymbolLocationEntry entry, string pattern)
    {
        var clean = SymbolNameMatcher.CleanPattern(pattern);
        if (string.Equals(entry.Name, clean, StringComparison.OrdinalIgnoreCase)) return 0;
        if (entry.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase)) return 1;
        if (entry.Name.Contains(clean, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static int GetScopeRank(SymbolLocationEntry entry)
    {
        return TestDetector.IsTestFile(entry.FilePath) ? 1 : 0;
    }

    private static string FormatEntriesText(IReadOnlyList<SymbolLocationEntry> entries, int totalMatches, int maxResults)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            var handoffPart = entry.HandoffId != null ? $" [handoff: {entry.HandoffId}]" : "";
            sb.AppendLine($"- {entry.Kind} {entry.Name} in {entry.FilePath}:{entry.Line} ({entry.ProjectName}){handoffPart}");
        }

        if (totalMatches > entries.Count)
        {
            sb.AppendLine($"\n... {totalMatches - entries.Count} weitere Treffer (maxResults={maxResults} erreicht).");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string> FormatMissMessageAsync(
        FindSymbolScanRequest request,
        IReadOnlyList<ISymbol> nameMatches,
        CancellationToken ct)
    {
        if (nameMatches.Count > 0)
        {
            var availableKinds = string.Join(", ", nameMatches.Select(DescribeKind).Distinct(StringComparer.OrdinalIgnoreCase));
            return $"Keine Treffer für '{request.NamePattern}' mit Filter '{request.Kind}'. Vorhandene Symbole mit diesem Namen haben den Typ: {availableKinds}.";
        }

        var suggestions = await SymbolNameMatcher.FindSimilarSymbolNamesAsync(request.Solution, request.NamePattern, ct).ConfigureAwait(false);
        if (suggestions.Count > 0)
        {
            return $"Keine Treffer für '{request.NamePattern}'. Meintest du eventuell: {string.Join(", ", suggestions)}?";
        }

        return $"Keine Treffer für '{request.NamePattern}' gefunden.";
    }
}
