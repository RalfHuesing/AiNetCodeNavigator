#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Sucht alle Referenzen und Implementierungen eines Symbols über die gesamte Solution hinweg.
/// </summary>
public static class FindReferencesResolver
{
    public static async Task<FindReferencesResult> FindReferencesAsync(
        ISymbol targetSymbol,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, ct).ConfigureAwait(false);

        var entries = new List<ReferenceLocationEntry>();

        foreach (var refSymbol in references)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var loc in refSymbol.Locations)
            {
                ct.ThrowIfCancellationRequested();
                if (loc.Document is not { } doc) continue;

                var lineSpan = loc.Location.GetLineSpan();
                var relPath = PathNormalizer.ToRelative(solutionDir, lineSpan.Path);
                var line = lineSpan.StartLinePosition.Line + 1;
                var column = lineSpan.StartLinePosition.Character + 1;

                var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
                var snippet = string.Empty;
                if (line <= text.Lines.Count)
                {
                    snippet = text.Lines[line - 1].ToString().Trim();
                }

                var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var enclosing = semanticModel?.GetEnclosingSymbol(loc.Location.SourceSpan.Start);

                var callerName = enclosing switch
                {
                    IMethodSymbol m => $"{m.ContainingType?.Name}.{m.Name}",
                    IPropertySymbol p => $"{p.ContainingType?.Name}.{p.Name}",
                    _ => enclosing?.Name ?? string.Empty
                };

                var callerHandoff = SourceHandoffFormatter.Format(enclosing, solution, handoffIdentity);

                entries.Add(new ReferenceLocationEntry(
                    FilePath: relPath,
                    Line: line,
                    Column: column,
                    Snippet: snippet,
                    EnclosingSymbolName: callerName,
                    EnclosingSymbolHandoffId: callerHandoff,
                    ProjectName: doc.Project.Name));
            }
        }

        var sorted = entries
            .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.Column)
            .ToList();

        var isTruncated = sorted.Count > normalizedMaxResults;
        var shown = sorted.Take(normalizedMaxResults).ToList();

        return new FindReferencesResult(
            TargetSymbolName: targetSymbol.Name,
            TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
            References: shown,
            TotalCount: sorted.Count,
            IsTruncated: isTruncated);
    }

    public static async Task<FindImplementationsResult> FindImplementationsAsync(
        ISymbol targetSymbol,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var handoffIdentity = await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var implementations = new List<ISymbol>();

        if (targetSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.TypeKind == TypeKind.Interface)
            {
                var impls = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(impls);
            }
            else
            {
                var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(derived);
            }
        }
        else
        {
            var impls = await SymbolFinder.FindImplementationsAsync(targetSymbol, solution, cancellationToken: ct).ConfigureAwait(false);
            implementations.AddRange(impls);

            if (targetSymbol is IMethodSymbol method && (method.IsAbstract || method.IsVirtual || method.IsOverride))
            {
                var overrides = await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(overrides);
            }
        }

        var entries = new List<ImplementationLocationEntry>();
        var distinctImpls = implementations.Distinct(SymbolEqualityComparer.Default).ToList();

        foreach (var impl in distinctImpls)
        {
            var loc = impl.Locations.FirstOrDefault(l => l.IsInSource);
            var filePath = loc?.SourceTree?.FilePath is not null
                ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
                : string.Empty;

            var line = loc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
            var handoff = SourceHandoffFormatter.Format(impl, solution, handoffIdentity);

            var projectName = impl.ContainingAssembly?.Name ?? string.Empty;
            var signature = impl.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            entries.Add(new ImplementationLocationEntry(
                SymbolName: impl.Name,
                Kind: impl.Kind.ToString().ToLowerInvariant(),
                FilePath: filePath,
                Line: line,
                Signature: signature,
                ProjectName: projectName,
                HandoffId: handoff));
        }

        var sorted = entries
            .OrderBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Line)
            .ThenBy(e => e.SymbolName, StringComparer.Ordinal)
            .ToList();
        var isTruncated = sorted.Count > normalizedMaxResults;
        var shown = sorted.Take(normalizedMaxResults).ToList();

        return new FindImplementationsResult(
            TargetSymbolName: targetSymbol.Name,
            TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
            Implementations: shown,
            TotalCount: sorted.Count,
            IsTruncated: isTruncated);
    }
}
