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
/// Ermittelt Basisklassen, Schnittstellen und abgeleitete bzw. implementierende Typen.
/// </summary>
public static class TypeHierarchyScanner
{
    public static async Task<TypeHierarchyPayload> ScanAsync(
        INamedTypeSymbol type,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var baseTypes = CollectBaseTypes(type, solution, solutionDir, handoffIdentity);
        var interfaces = CollectInterfaces(type, solution, solutionDir, handoffIdentity);

        var isInterface = type.TypeKind == TypeKind.Interface;
        var subtypesHeading = isInterface ? "Implementierende Typen:" : "Abgeleitete Klassen:";

        var subtypesSymbols = isInterface
            ? (await SymbolFinder.FindImplementationsAsync(type, solution, cancellationToken: ct).ConfigureAwait(false)).OfType<INamedTypeSymbol>().ToList()
            : (await SymbolFinder.FindDerivedClassesAsync(type, solution, cancellationToken: ct).ConfigureAwait(false)).ToList();

        var subtypeEntries = subtypesSymbols
            .Select(s => CreateEntry(s, solution, solutionDir, handoffIdentity))
            .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        var isTruncated = subtypeEntries.Count > maxResults;
        var shown = subtypeEntries.Take(maxResults).ToList();

        return new TypeHierarchyPayload(
            TypeName: type.ToDisplayString(),
            BaseTypes: baseTypes,
            Interfaces: interfaces,
            SubtypesHeading: subtypesHeading,
            Subtypes: shown,
            TotalSubtypes: subtypeEntries.Count,
            IsTruncated: isTruncated);
    }

    private static List<TypeHierarchyEntry> CollectBaseTypes(INamedTypeSymbol type, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
    {
        var list = new List<TypeHierarchyEntry>();
        var current = type.BaseType;

        while (current != null)
        {
            list.Add(CreateEntry(current, solution, solutionDir, identity));
            current = current.BaseType;
        }

        return list;
    }

    private static List<TypeHierarchyEntry> CollectInterfaces(INamedTypeSymbol type, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
    {
        return type.AllInterfaces
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .Select(i => CreateEntry(i, solution, solutionDir, identity))
            .ToList();
    }

    private static TypeHierarchyEntry CreateEntry(INamedTypeSymbol symbol, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
    {
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        var filePath = loc?.SourceTree?.FilePath is not null
            ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
            : string.Empty;

        var line = loc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
        var handoff = SourceHandoffFormatter.Format(symbol, solution, identity);

        var kind = symbol.IsRecord
            ? (symbol.TypeKind == TypeKind.Struct ? "record struct" : "record")
            : symbol.TypeKind.ToString().ToLowerInvariant();

        return new TypeHierarchyEntry(
            Name: symbol.ToDisplayString(),
            Kind: kind,
            FilePath: filePath,
            Line: line,
            HandoffId: handoff);
    }
}
