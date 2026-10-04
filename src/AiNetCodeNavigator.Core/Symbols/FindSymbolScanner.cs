#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Search engine for C# symbols through Roslyn Solution and SymbolFinder.
/// Filters by name, pattern, SymbolKind and ScopeType (production/tests).
/// </summary>
public static class FindSymbolScanner
{
    public static async Task<FindSymbolScanResult> FindMatchesWithDetailsAsync(
        FindSymbolScanRequest request,
        CancellationToken ct = default)
    {
        var currentSourceIdentity = request.CurrentIdentityRequest is { } currentRequest
            && currentRequest.IsForSolution(request.Solution) ? currentRequest.Identity : null;
        if (request.SourceIdentity is not null
            && (currentSourceIdentity is null
                ? new ResultError(NavigationErrorCodes.TargetMismatch,
                    "A caller-supplied source identity requires a current validated runtime identity request.")
                : AnalysisSymbolIdentity.SourceMismatch(request.SourceIdentity, currentSourceIdentity)) is { } identityError)
        {
            return new FindSymbolScanResult(
                identityError.Message,
                Array.Empty<SymbolLocationEntry>(),
                0,
                0,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                identityError);
        }

        var nameFilter = SymbolNameMatcher.CreateDeclarationNameFilter(request.NamePattern);
        var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
            request.Solution,
            nameFilter,
            SymbolFilter.TypeAndMember,
            ct).ConfigureAwait(false);
        var generatedDocumentOwners = await ExactSourceSymbolResolver.GetSourceGeneratedDocumentOwnersAsync(request.Solution, ct)
            .ConfigureAwait(false);
        var generatedDeclarations = await GetGeneratedDeclarationsAsync(generatedDocumentOwners, request.NamePattern, ct)
            .ConfigureAwait(false);

        var nameMatches = symbols.Concat(generatedDeclarations).Distinct(SymbolEqualityComparer.Default)
            .Where(symbol => HasCSharpSourceLocation(request.Solution, symbol, generatedDocumentOwners))
            .Where(symbol => SymbolNameMatcher.MatchesSymbol(symbol, request.NamePattern))
            .ToList();

        var filtered = FilterByKind(nameMatches, request.Kind)
            .Where(symbol => request.ProjectId is null || symbol.Locations.Any(location => location.SourceTree is not null && GetSourceDocument(request.Solution, location.SourceTree, generatedDocumentOwners)?.Project.Id == request.ProjectId))
            .Where(symbol => !request.ExtensionOnly || symbol is IMethodSymbol { IsExtensionMethod: true })
            .Where(symbol => string.IsNullOrWhiteSpace(request.NamespaceFilter) || symbol.ContainingNamespace.ToDisplayString().Contains(request.NamespaceFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(symbol => string.IsNullOrWhiteSpace(request.SignatureFilter) || symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).Contains(request.SignatureFilter, StringComparison.Ordinal))
            .Where(symbol => !request.ExtensionOnly || symbol is IMethodSymbol method && DeclaredExtensionReceiverMatcher.Matches(method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), request.ReceiverType))
            .ToList();
        var kindAlternatives = request.ExtensionOnly ? Array.Empty<string>() : CreateKindAlternatives(nameMatches, request.Kind);

        if (filtered.Count == 0)
        {
            var missMessage = request.ExtensionOnly
                ? $"No declared extension methods matched '{request.NamePattern}' and the selected filters."
                : await FormatMissMessageAsync(request, nameMatches, generatedDocumentOwners, ct).ConfigureAwait(false);
            return new FindSymbolScanResult(missMessage, Array.Empty<SymbolLocationEntry>(), 0, 0, false, Array.Empty<string>(), kindAlternatives);
        }

        var outputRoot = Path.GetDirectoryName(request.Solution.FilePath) ?? string.Empty;
        var formatter = request.CurrentIdentityRequest is { } exactRequest && exactRequest.IsForSolution(request.Solution)
            ? new Func<ISymbol, string?>(symbol => exactRequest.FormatHandoff(symbol, request.Solution))
            : await SourceReferenceFormattingContext.CreateFormatterAsync(request.Solution, ct).ConfigureAwait(false);
        var allEntries = (await BuildVisibleEntriesAsync(request, filtered, outputRoot, formatter,
                generatedDocumentOwners, ct).ConfigureAwait(false))
            .OrderBy(entry => GetMatchRank(entry, request.NamePattern))
            .ThenBy(entry => GetScopeRank(entry))
            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Line)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        if (allEntries.Count == 0)
        {
            return new FindSymbolScanResult(
                $"No matches for '{request.NamePattern}' in the requested scope '{request.ScopeType}'.",
                Array.Empty<SymbolLocationEntry>(),
                0,
                0,
                false,
                Array.Empty<string>(),
                kindAlternatives);
        }

        var identity = currentSourceIdentity;
        if (identity is null && (request.ResultCursor is not null || allEntries.Count > request.MaxResults))
        {
            var error = new ResultError(NavigationErrorCodes.TargetMismatch,
                "A stable source target identity is required to continue a bounded symbol result list.",
                "Load the source solution through a canonical .sln or .slnx target and repeat the query.");
            return new FindSymbolScanResult(error.Message, [], allEntries.Count, 0, false, [], kindAlternatives, error);
        }
        var binding = identity is null ? string.Empty : BoundResultCursor.CreateBinding(
            identity.CanonicalPath,
            identity.ContentHash,
            "find_symbol",
            request.NamePattern,
            request.Kind.ToString(),
            request.ScopeType.ToString(),
            request.IncludeGenerated.ToString(),
            request.MaxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.ProjectId?.ToString(), request.NamespaceFilter, request.SignatureFilter, request.ExtensionOnly.ToString(), request.ReceiverType);
        var cursorStatus = BoundResultCursor.ReadOffset(request.ResultCursor, binding, out var offset);
        if (cursorStatus != BoundResultCursor.CursorStatus.Valid)
        {
            var code = cursorStatus == BoundResultCursor.CursorStatus.InvalidFormat
                ? NavigationErrorCodes.InvalidArgument
                : NavigationErrorCodes.StaleSnapshot;
            var error = new ResultError(code,
                cursorStatus == BoundResultCursor.CursorStatus.InvalidFormat
                    ? "resultCursor is invalid."
                    : "resultCursor is not bound to this source snapshot and search query.",
                "Repeat the same search against the same source snapshot using its most recent resultCursor.");
            return new FindSymbolScanResult(error.Message, [], allEntries.Count, 0, false, [], kindAlternatives, error);
        }

        var page = BoundResultCursor.Page(allEntries, offset, request.MaxResults, binding);
        var collectedEntries = page.Items;
        var isTruncated = page.NextCursor is not null;
        var text = FormatEntriesText(collectedEntries, page.TotalCount, request.MaxResults);

        return new FindSymbolScanResult(
            text,
            collectedEntries,
            page.TotalCount,
            collectedEntries.Length,
            isTruncated,
            isTruncated ? ["maxResults"] : Array.Empty<string>(),
            kindAlternatives,
            ResultCursor: page.NextCursor);
    }

    private static async Task<IReadOnlyList<SymbolLocationEntry>> BuildVisibleEntriesAsync(
        FindSymbolScanRequest request,
        IReadOnlyList<ISymbol> symbols,
        string outputRoot,
        Func<ISymbol, string?>? handoffFormatter,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        CancellationToken ct)
    {
        var grouped = GroupSymbols(symbols);
        var entries = new List<SymbolLocationEntry>(grouped.Count);

        foreach (var (symbol, declarations) in grouped)
        {
            ct.ThrowIfCancellationRequested();

            var locations = await CollectVisibleLocationsAsync(request, declarations, outputRoot,
                generatedDocumentOwners, ct).ConfigureAwait(false);
            if (locations.Count == 0) continue;

            locations.Sort((a, b) =>
            {
                var cmp = string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : a.Line.CompareTo(b.Line);
            });

            var primaryLoc = locations[0];
            var docCommentId = symbol.GetDocumentationCommentId();
            var handoffId = handoffFormatter?.Invoke(symbol);

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
                Locations: locations,
                IsExtension: symbol is IMethodSymbol { IsExtensionMethod: true },
                Namespace: symbol.ContainingNamespace.ToDisplayString(),
                ContainingType: symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ReceiverType: symbol is IMethodSymbol { IsExtensionMethod: true } extension ? extension.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) : null,
                ReturnType: symbol is IMethodSymbol method ? method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) : null));
        }

        return entries;
    }

    private static async Task<List<SymbolLocationItem>> CollectVisibleLocationsAsync(
        FindSymbolScanRequest request,
        IReadOnlyList<ISymbol> declarations,
        string outputRoot,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        CancellationToken cancellationToken)
    {
        var result = new List<SymbolLocationItem>();
        var generatedDocuments = new Dictionary<DocumentId, bool>();
        foreach (var decl in declarations)
        {
            foreach (var loc in decl.Locations)
            {
                if (!loc.IsInSource || loc.SourceTree is null) continue;
                var filePath = loc.SourceTree.FilePath;
                var document = GetSourceDocument(request.Solution, loc.SourceTree, generatedDocumentOwners);
                if (document is null || !IsCSharpProject(document.Project) || !MatchesScope(document, request.ScopeType)
                    || request.ProjectId is not null && document.Project.Id != request.ProjectId) continue;
                if (!request.IncludeGenerated)
                {
                    if (!generatedDocuments.TryGetValue(document.Id, out var isGenerated))
                    {
                        isGenerated = document is SourceGeneratedDocument
                            || await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, cancellationToken).ConfigureAwait(false);
                        generatedDocuments.Add(document.Id, isGenerated);
                    }

                    if (isGenerated) continue;
                }

                var relativePath = PathNormalizer.ToRelative(outputRoot, filePath);
                var lineSpan = loc.GetLineSpan();
                var startLine = lineSpan.StartLinePosition.Line + 1;
                var endLine = lineSpan.EndLinePosition.Line + 1;
                var projectName = document.Project.Name;

                result.Add(new SymbolLocationItem(relativePath, startLine, endLine, projectName));
            }
        }

        return result;
    }

    private static bool HasCSharpSourceLocation(
        Solution solution,
        ISymbol symbol,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners)
    {
        return symbol.Locations.Any(location =>
            location.IsInSource
            && location.SourceTree is not null
            && GetSourceDocument(solution, location.SourceTree, generatedDocumentOwners) is { } document
            && IsCSharpProject(document.Project));
    }

    private static Document? GetSourceDocument(
        Solution solution,
        SyntaxTree syntaxTree,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners) =>
        solution.GetDocument(syntaxTree)
        ?? (generatedDocumentOwners.TryGetValue(syntaxTree, out var generatedDocument) ? generatedDocument : null);

    private static async Task<IReadOnlyList<ISymbol>> GetGeneratedDeclarationsAsync(
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        string namePattern,
        CancellationToken cancellationToken)
    {
        var results = new List<ISymbol>();
        foreach (var document in generatedDocumentOwners.Values.DistinctBy(document => document.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null) continue;

            foreach (var declaration in root.DescendantNodesAndSelf().OfType<MemberDeclarationSyntax>())
                AddMatching(semanticModel.GetDeclaredSymbol(declaration, cancellationToken));
            foreach (var declaration in root.DescendantNodesAndSelf().OfType<EnumMemberDeclarationSyntax>())
                AddMatching(semanticModel.GetDeclaredSymbol(declaration, cancellationToken));
            foreach (var variable in root.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
                    AddMatching(semanticModel.GetDeclaredSymbol(variable, cancellationToken));
            }
        }

        return results;

        void AddMatching(ISymbol? symbol)
        {
            if (symbol is not null
                && symbol is not INamespaceSymbol
                && SymbolNameMatcher.MatchesSymbol(symbol, namePattern)
                && !results.Contains(symbol, SymbolEqualityComparer.Default))
                results.Add(symbol);
        }
    }

    private static bool IsCSharpProject(Project project) =>
        string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);

    private static bool MatchesScope(Document document, SymbolScopeType scopeType)
    {
        if (scopeType == SymbolScopeType.All) return true;

        var isTest = TestDetector.IsTestProject(document.Project)
            || TestDetector.IsTestFile(document.FilePath ?? document.Name);
        return scopeType switch
        {
            SymbolScopeType.Production => !isTest,
            SymbolScopeType.Tests => isTest,
            _ => false
        };
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
            SymbolKindFilter.RecordClass => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class, IsRecord: true }),
            SymbolKindFilter.RecordStruct => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRecord: true }),
            SymbolKindFilter.Struct => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRecord: false }),
            SymbolKindFilter.Interface => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Interface }),
            SymbolKindFilter.Enum => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Enum }),
            SymbolKindFilter.Delegate => symbols.Where(s => s is INamedTypeSymbol { TypeKind: TypeKind.Delegate }),
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
            INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            INamedTypeSymbol { IsRecord: true } => "record class",
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
            sb.AppendLine($"\n... {totalMatches - entries.Count} more matches (maxResults={maxResults} reached).");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<string> FormatMissMessageAsync(
        FindSymbolScanRequest request,
        IReadOnlyList<ISymbol> nameMatches,
        IReadOnlyDictionary<SyntaxTree, SourceGeneratedDocument> generatedDocumentOwners,
        CancellationToken ct)
    {
        if (nameMatches.Count > 0)
        {
            var availableKinds = string.Join(", ", nameMatches.Select(DescribeKind).Distinct(StringComparer.OrdinalIgnoreCase));
            return $"No matches for '{request.NamePattern}' with filter '{request.Kind}'. Existing symbols with this name have kind: {availableKinds}.";
        }

        var suggestions = await SymbolNameMatcher.FindSimilarSymbolNamesAsync(
            request.Solution,
            request.NamePattern,
            ct,
            symbol => HasCSharpSourceLocation(request.Solution, symbol, generatedDocumentOwners)).ConfigureAwait(false);
        if (suggestions.Count > 0)
        {
            return $"No matches for '{request.NamePattern}'. Did you mean: {string.Join(", ", suggestions)}?";
        }

        return $"No matches found for '{request.NamePattern}'.";
    }
}
