#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Hierarchy;

/// <summary>
/// Identifies base classes, interfaces and derived or implementing types.
/// </summary>
public static class TypeHierarchyScanner
{
    public static async Task<TypeHierarchyPayload> ScanAsync(
        INamedTypeSymbol type,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default,
        SymbolScopeType scope = SymbolScopeType.All,
        bool includeGenerated = false,
        Func<ISymbol, string?>? handoffFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(solution);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var handoffIdentity = handoffFormatter is null
            ? await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false)
            : null;
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct))
        {
            return new TypeHierarchyPayload(
                TypeName: type.ToDisplayString(),
                BaseTypes: Array.Empty<TypeHierarchyEntry>(),
                Interfaces: Array.Empty<TypeHierarchyEntry>(),
                SubtypesHeading: "Derived classes:",
                Subtypes: Array.Empty<TypeHierarchyEntry>(),
                TotalSubtypes: 0,
                IsTruncated: false,
                ErrorMessage: $"Type '{type.ToDisplayString()}' ({type.TypeKind.ToString().ToLowerInvariant()}) is not supported; classes, interfaces or structs are expected.");
        }

        var baseTypes = CollectBaseTypes(type, solution, solutionDir, handoffIdentity, handoffFormatter, ct);
        var interfaces = CollectInterfaces(type, solution, solutionDir, handoffIdentity, handoffFormatter);

        var isInterface = type.TypeKind == TypeKind.Interface;
        var subtypesHeading = isInterface ? "Implementing types:" : "Derived classes:";

        var subtypesSymbols = isInterface
            ? (await SymbolFinder.FindImplementationsAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false)).OfType<INamedTypeSymbol>().ToList()
            : type.TypeKind == TypeKind.Class
                ? (await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false)).ToList()
                : [];

        var visibleSubtypes = new List<INamedTypeSymbol>();
        foreach (var subtype in subtypesSymbols)
        {
            ct.ThrowIfCancellationRequested();
            var document = subtype.DeclaringSyntaxReferences.Select(reference => solution.GetDocument(reference.SyntaxTree)).FirstOrDefault(item => item is not null);
            if (document is not null)
            {
                var isTest = TestDetector.IsTestProject(document.Project) || TestDetector.IsTestFile(document.FilePath);
                if ((scope == SymbolScopeType.Production && isTest) || (scope == SymbolScopeType.Tests && !isTest)) continue;
                if (!includeGenerated && await GeneratedDocumentDetector.IsGeneratedDocumentAsync(document, ct).ConfigureAwait(false)) continue;
            }
            else if (scope != SymbolScopeType.All) continue;
            visibleSubtypes.Add(subtype);
        }

        var subtypeEntries = visibleSubtypes
            .Select(s => CreateEntries(s, solution, solutionDir, handoffIdentity, handoffFormatter).First())
            .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.FilePath, StringComparer.Ordinal)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        var isTruncated = subtypeEntries.Count > normalizedMaxResults;
        var shown = subtypeEntries.Take(normalizedMaxResults).ToList();

        return new TypeHierarchyPayload(
            TypeName: type.ToDisplayString(),
            BaseTypes: baseTypes,
            Interfaces: interfaces,
            SubtypesHeading: subtypesHeading,
            Subtypes: shown,
            TotalSubtypes: subtypeEntries.Count,
            IsTruncated: isTruncated);
    }

    private static List<TypeHierarchyEntry> CollectBaseTypes(INamedTypeSymbol type, Solution solution, string solutionDir,
        AnalysisSymbolIdentity? identity, Func<ISymbol, string?>? handoffFormatter, CancellationToken ct)
    {
        var list = new List<TypeHierarchyEntry>();
        var current = type.BaseType;
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        while (current != null && visited.Add(current.OriginalDefinition))
        {
            ct.ThrowIfCancellationRequested();
            list.AddRange(CreateEntries(current, solution, solutionDir, identity, handoffFormatter));
            current = current.BaseType;
        }

        return list;
    }

    private static List<TypeHierarchyEntry> CollectInterfaces(INamedTypeSymbol type, Solution solution, string solutionDir,
        AnalysisSymbolIdentity? identity, Func<ISymbol, string?>? handoffFormatter)
    {
        return type.AllInterfaces
            .OrderBy(i => i.ToDisplayString(), StringComparer.Ordinal)
            .SelectMany(i => CreateEntries(i, solution, solutionDir, identity, handoffFormatter))
            .ToList();
    }

    private static IEnumerable<TypeHierarchyEntry> CreateEntries(
        INamedTypeSymbol symbol,
        Solution solution,
        string solutionDir,
        AnalysisSymbolIdentity? identity,
        Func<ISymbol, string?>? handoffFormatter)
    {
        var locations = symbol.Locations
            .Where(location => location.IsInSource)
            .OrderBy(location => location.SourceTree?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Line)
            .ToList();
        var handoff = handoffFormatter is null
            ? StableSourceReferenceFormatter.Format(symbol, solution, identity)
            : handoffFormatter(symbol);
        var kind = symbol.IsRecord
            ? (symbol.TypeKind == TypeKind.Struct ? "record struct" : "record")
            : symbol.TypeKind.ToString().ToLowerInvariant();

        if (locations.Count == 0)
        {
            yield return new TypeHierarchyEntry(
                Name: symbol.ToDisplayString(),
                Kind: kind,
                FilePath: string.Empty,
                Line: 0,
                HandoffId: handoff);
            yield break;
        }

        foreach (var location in locations)
        {
            var filePath = location.SourceTree?.FilePath is not null
                ? PathNormalizer.ToRelative(solutionDir, location.SourceTree.FilePath)
                : string.Empty;
            var line = location.GetLineSpan().StartLinePosition.Line + 1;
            yield return new TypeHierarchyEntry(
                Name: symbol.ToDisplayString(),
                Kind: kind,
                FilePath: filePath,
                Line: line,
                HandoffId: handoff);
        }
    }
}
