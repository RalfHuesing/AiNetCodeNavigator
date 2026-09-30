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
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(solution);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct))
        {
            return new TypeHierarchyPayload(
                TypeName: type.ToDisplayString(),
                BaseTypes: Array.Empty<TypeHierarchyEntry>(),
                Interfaces: Array.Empty<TypeHierarchyEntry>(),
                SubtypesHeading: "Abgeleitete Klassen:",
                Subtypes: Array.Empty<TypeHierarchyEntry>(),
                TotalSubtypes: 0,
                IsTruncated: false,
                ErrorMessage: $"Typ '{type.ToDisplayString()}' ({type.TypeKind.ToString().ToLowerInvariant()}) wird nicht unterstützt; erwartet werden Klassen, Interfaces oder Structs.");
        }

        var baseTypes = CollectBaseTypes(type, solution, solutionDir, handoffIdentity, ct);
        var interfaces = CollectInterfaces(type, solution, solutionDir, handoffIdentity);

        var isInterface = type.TypeKind == TypeKind.Interface;
        var subtypesHeading = isInterface ? "Implementierende Typen:" : "Abgeleitete Klassen:";

        var subtypesSymbols = isInterface
            ? (await SymbolFinder.FindImplementationsAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false)).OfType<INamedTypeSymbol>().ToList()
            : type.TypeKind == TypeKind.Class
                ? (await SymbolFinder.FindDerivedClassesAsync(type, solution, transitive: true, cancellationToken: ct).ConfigureAwait(false)).ToList()
                : [];

        var subtypeEntries = subtypesSymbols
            .Select(s => CreateEntry(s, solution, solutionDir, handoffIdentity))
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

    private static List<TypeHierarchyEntry> CollectBaseTypes(INamedTypeSymbol type, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity, CancellationToken ct)
    {
        var list = new List<TypeHierarchyEntry>();
        var current = type.BaseType;
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        while (current != null && visited.Add(current.OriginalDefinition))
        {
            ct.ThrowIfCancellationRequested();
            list.Add(CreateEntry(current, solution, solutionDir, identity));
            current = current.BaseType;
        }

        return list;
    }

    private static List<TypeHierarchyEntry> CollectInterfaces(INamedTypeSymbol type, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
    {
        return type.AllInterfaces
            .OrderBy(i => i.ToDisplayString(), StringComparer.Ordinal)
            .Select(i => CreateEntry(i, solution, solutionDir, identity))
            .ToList();
    }

    private static TypeHierarchyEntry CreateEntry(INamedTypeSymbol symbol, Solution solution, string solutionDir, AnalysisSymbolIdentity? identity)
    {
        var loc = symbol.Locations
            .Where(location => location.IsInSource)
            .OrderBy(location => location.SourceTree?.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(location => location.GetLineSpan().StartLinePosition.Line)
            .FirstOrDefault();
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
