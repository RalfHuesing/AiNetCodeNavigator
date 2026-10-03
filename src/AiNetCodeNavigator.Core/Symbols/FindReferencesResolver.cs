#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Finds all references and implementations of a symbol across the entire solution.
/// </summary>
public static class FindReferencesResolver
{
    public const int MaxReferenceDepth = 3;
    public const int DefaultMaxVisitedSymbols = 200;

    public static async Task<FindReferencesResult> FindReferencesAsync(
        ISymbol targetSymbol,
        Solution solution,
        int maxResults,
        int depth,
        CancellationToken ct = default,
        int maxNodes = DefaultMaxVisitedSymbols,
        SymbolScopeType scope = SymbolScopeType.All,
        bool includeGenerated = false,
        Func<ISymbol, string?>? handoffFormatter = null,
        string? ownerTargetPath = null)
        => await FindReferencesAsyncCore(targetSymbol, solution, maxResults, depth, maxNodes, ct, scope, includeGenerated, handoffFormatter, ownerTargetPath).ConfigureAwait(false);

    private static async Task<FindReferencesResult> FindReferencesAsyncCore(
        ISymbol targetSymbol,
        Solution solution,
        int maxResults,
        int requestedDepth,
        int maxNodes,
        CancellationToken ct,
        SymbolScopeType scope,
        bool includeGenerated,
        Func<ISymbol, string?>? handoffFormatter = null,
        string? ownerTargetPath = null)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var effectiveDepth = Math.Clamp(requestedDepth, 1, MaxReferenceDepth);
        var effectiveNodeLimit = Math.Min(maxNodes, DefaultMaxVisitedSymbols);
        var handoffIdentity = handoffFormatter is null
            ? await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false)
            : null;
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var entries = new List<(ReferenceLocationEntry Entry, ISymbol? CallerSymbol, ISymbol ReachedFromSymbol,
            string CallerProjectPath, string CallerProjectId, string CallerSymbolId, string ReachedFromSymbolId)>();
        var queue = new Queue<(ISymbol Symbol, int Depth)>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { targetSymbol };
        queue.Enqueue((targetSymbol, 1));
        var truncatedByNodeLimit = false;
        var expandedSymbolCount = 0;
        var generatedDocuments = new Dictionary<DocumentId, bool>();

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (expandedSymbolCount >= effectiveNodeLimit)
            {
                truncatedByNodeLimit = true;
                break;
            }

            var (currentSymbol, currentDepth) = queue.Dequeue();
            expandedSymbolCount++;
            var references = await SymbolFinder.FindReferencesAsync(currentSymbol, solution, ct).ConfigureAwait(false);

            foreach (var refSymbol in references)
            {
                foreach (var loc in refSymbol.Locations)
                {
                    ct.ThrowIfCancellationRequested();
                    if (loc.Document is not { } doc || !loc.Location.IsInSource) continue;
                    var isTest = TestDetector.IsTestProject(doc.Project) || TestDetector.IsTestFile(doc.FilePath);
                    if ((scope == SymbolScopeType.Production && isTest) || (scope == SymbolScopeType.Tests && !isTest)) continue;
                    if (!includeGenerated)
                    {
                        if (!generatedDocuments.TryGetValue(doc.Id, out var isGenerated))
                        {
                            isGenerated = await GeneratedDocumentDetector.IsGeneratedDocumentAsync(doc, ct).ConfigureAwait(false);
                            generatedDocuments[doc.Id] = isGenerated;
                        }
                        if (isGenerated) continue;
                    }

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
                    var enclosing = semanticModel is null ? null : ResolveEnclosingSourceSymbol(semanticModel, loc.Location);
                    var syntaxRoot = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                    var referenceNode = syntaxRoot?.FindNode(loc.Location.SourceSpan, getInnermostNodeForTie: true);

                    var callerName = enclosing switch
                    {
                        IMethodSymbol m => $"{m.ContainingType?.Name}.{m.Name}",
                        IPropertySymbol p => $"{p.ContainingType?.Name}.{p.Name}",
                        _ => enclosing?.Name ?? string.Empty
                    };

                    var reachedFromName = currentSymbol switch
                    {
                        IMethodSymbol method => $"{method.ContainingType?.Name}.{method.Name}",
                        IPropertySymbol property => $"{property.ContainingType?.Name}.{property.Name}",
                        _ => currentSymbol.Name
                    };

                    entries.Add((new ReferenceLocationEntry(
                            FilePath: relPath,
                            Line: line,
                            Column: column,
                            Snippet: snippet,
                            EnclosingSymbolName: callerName,
                            EnclosingSymbolHandoffId: null,
                            ProjectName: doc.Project.Name,
                            Depth: currentDepth,
                            ReachedFromSymbolName: reachedFromName,
                            ReachedFromSymbolHandoffId: null,
                            OwnerTargetPath: ownerTargetPath,
                        ReachedFromSymbolId: RelationshipSymbolIdentity.GetStableId(currentSymbol),
                        EvidenceKind: referenceNode is null
                            ? RelationshipEvidence.Unresolved
                            : RelationshipEvidence.Classify(referenceNode, semanticModel)),
                        enclosing,
                        currentSymbol,
                        doc.Project.FilePath is { Length: > 0 } projectPath ? Path.GetFullPath(projectPath) : string.Empty,
                        doc.Project.Id.Id.ToString("N"),
                        enclosing is null ? string.Empty : RelationshipSymbolIdentity.GetStableId(enclosing),
                        RelationshipSymbolIdentity.GetStableId(currentSymbol)));

                    if (currentDepth >= effectiveDepth || enclosing is null) continue;
                    var caller = NormalizeToOwningMember(enclosing);
                    if (caller is null || visited.Contains(caller)) continue;
                    if (visited.Count >= effectiveNodeLimit)
                    {
                        truncatedByNodeLimit = true;
                        continue;
                    }

                    visited.Add(caller);
                    queue.Enqueue((caller, currentDepth + 1));
                }
            }
        }

        var sorted = entries
            .GroupBy(item => (item.Entry, item.CallerProjectId, item.CallerSymbolId, item.ReachedFromSymbolId))
            .Select(group => group.First())
            .OrderBy(item => item.Entry.Depth)
            .ThenBy(item => item.CallerProjectPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CallerProjectPath, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Line)
            .ThenBy(item => item.Entry.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.EnclosingSymbolName, StringComparer.Ordinal)
            .ThenBy(item => item.CallerProjectId, StringComparer.Ordinal)
            .ThenBy(item => item.CallerSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.ReachedFromSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Column)
            .Select(item => item.Entry)
            .ToList();

        var isTruncated = sorted.Count > normalizedMaxResults;
        var shown = entries
            .GroupBy(item => (item.Entry, item.CallerProjectId, item.CallerSymbolId, item.ReachedFromSymbolId))
            .Select(group => group.First())
            .OrderBy(item => item.Entry.Depth)
            .ThenBy(item => item.CallerProjectPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CallerProjectPath, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Line)
            .ThenBy(item => item.Entry.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.EnclosingSymbolName, StringComparer.Ordinal)
            .ThenBy(item => item.CallerProjectId, StringComparer.Ordinal)
            .ThenBy(item => item.CallerSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.ReachedFromSymbolId, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Column)
            .Take(normalizedMaxResults)
            .Select(item => item.Entry with
            {
                EnclosingSymbolHandoffId = item.CallerSymbol is null
                    ? null
                    : handoffFormatter is null
                        ? StableSourceReferenceFormatter.Format(item.CallerSymbol, solution, handoffIdentity)
                        : handoffFormatter(item.CallerSymbol),
                ReachedFromSymbolHandoffId = handoffFormatter is null
                    ? StableSourceReferenceFormatter.Format(item.ReachedFromSymbol, solution, handoffIdentity)
                    : handoffFormatter(item.ReachedFromSymbol),
            })
            .ToList();

        return new FindReferencesResult(
            TargetSymbolName: targetSymbol.Name,
            TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
            References: shown,
            TotalCount: sorted.Count,
            IsTruncated: isTruncated,
            RequestedDepth: requestedDepth,
            EffectiveDepth: effectiveDepth,
            VisitedSymbolCount: expandedSymbolCount,
            IsTruncatedByNodeLimit: truncatedByNodeLimit,
            IsDepthClamped: requestedDepth != effectiveDepth,
            EffectiveNodeLimit: effectiveNodeLimit);
    }

    private static ISymbol? NormalizeToOwningMember(ISymbol? symbol) =>
        symbol is IMethodSymbol { AssociatedSymbol: { } owner } ? owner : symbol;

    private static ISymbol? ResolveEnclosingSourceSymbol(SemanticModel semanticModel, Location location)
    {
        var enclosing = semanticModel.GetEnclosingSymbol(location.SourceSpan.Start);
        if (enclosing is not null and not INamespaceSymbol) return enclosing;

        var node = location.SourceTree?.GetRoot().FindNode(location.SourceSpan, getInnermostNodeForTie: true);
        var declared = node?.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .Select(member => semanticModel.GetDeclaredSymbol(member))
            .FirstOrDefault(symbol => symbol is not null);
        return declared ?? enclosing;
    }

    public static async Task<FindImplementationsResult> FindImplementationsAsync(
        ISymbol targetSymbol,
        Solution solution,
        int maxResults = 50,
        CancellationToken ct = default,
        SymbolScopeType scope = SymbolScopeType.All,
        bool includeGenerated = false,
        Func<ISymbol, string?>? handoffFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(targetSymbol);
        ArgumentNullException.ThrowIfNull(solution);

        var normalizedMaxResults = Math.Max(maxResults, 1);
        var handoffIdentity = handoffFormatter is null
            ? await AnalysisSymbolIdentity.ForSourceAsync(solution, ct).ConfigureAwait(false)
            : null;
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var implementations = new List<ISymbol>();
        string? errorMessage = null;

        if (targetSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.TypeKind == TypeKind.Interface)
            {
                var impls = await SymbolFinder.FindImplementationsAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(impls);
            }
            else if (namedType.TypeKind == TypeKind.Class)
            {
                var derived = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(derived);
            }
            else
            {
                errorMessage = $"Type '{namedType.ToDisplayString()}' ({namedType.TypeKind.ToString().ToLowerInvariant()}) is neither an interface nor an inheritable class.";
            }
        }
        else if (targetSymbol is IMethodSymbol method)
        {
            if (method.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var impls = await SymbolFinder.FindImplementationsAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(impls);
            }
            else if (method.IsAbstract || method.IsVirtual || method.IsOverride)
            {
                var overrides = await SymbolFinder.FindOverridesAsync(method, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(overrides);
            }
            else
            {
                errorMessage = $"Method '{method.ToDisplayString()}' is neither an interface member nor virtual/abstract.";
            }
        }
        else if (targetSymbol is IPropertySymbol property)
        {
            if (property.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var impls = await SymbolFinder.FindImplementationsAsync(property, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(impls);
            }
            else if (property.IsAbstract || property.IsVirtual || property.IsOverride)
            {
                var overrides = await SymbolFinder.FindOverridesAsync(property, solution, cancellationToken: ct).ConfigureAwait(false);
                implementations.AddRange(overrides);
            }
            else
            {
                errorMessage = $"Property '{property.ToDisplayString()}' is neither an interface member nor virtual/abstract.";
            }
        }
        else
        {
            errorMessage = $"Symbol '{targetSymbol.ToDisplayString()}' ({targetSymbol.Kind}) cannot have implementations or overrides.";
        }

        if (errorMessage is not null)
        {
            return new FindImplementationsResult(
                TargetSymbolName: targetSymbol.Name,
                TargetKind: targetSymbol.Kind.ToString().ToLowerInvariant(),
                Implementations: Array.Empty<ImplementationLocationEntry>(),
                TotalCount: 0,
                IsTruncated: false,
                ErrorMessage: errorMessage);
        }

        var entries = new List<ImplementationLocationEntry>();
        var distinctImpls = implementations.Distinct(SymbolEqualityComparer.Default).ToList();
        var generatedDocuments = new Dictionary<DocumentId, bool>();

        foreach (var impl in distinctImpls)
        {
            var loc = impl.Locations.FirstOrDefault(l => l.IsInSource);
            var sourceDocument = loc?.SourceTree is { } tree ? solution.GetDocument(tree) : null;
            if (sourceDocument is not null)
            {
                var isTest = TestDetector.IsTestProject(sourceDocument.Project) || TestDetector.IsTestFile(sourceDocument.FilePath);
                if ((scope == SymbolScopeType.Production && isTest) || (scope == SymbolScopeType.Tests && !isTest)) continue;
                if (!includeGenerated)
                {
                    if (!generatedDocuments.TryGetValue(sourceDocument.Id, out var isGenerated))
                    {
                        isGenerated = await GeneratedDocumentDetector.IsGeneratedDocumentAsync(sourceDocument, ct).ConfigureAwait(false);
                        generatedDocuments[sourceDocument.Id] = isGenerated;
                    }
                    if (isGenerated) continue;
                }
            }
            var filePath = loc?.SourceTree?.FilePath is not null
                ? PathNormalizer.ToRelative(solutionDir, loc.SourceTree.FilePath)
                : string.Empty;

            var line = loc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
            var handoff = handoffFormatter is null
                ? StableSourceReferenceFormatter.Format(impl, solution, handoffIdentity)
                : handoffFormatter(impl);

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
            IsTruncated: isTruncated,
            ErrorMessage: null);
    }
}
