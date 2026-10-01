#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Resolves raw declaration identifiers only within a selected assembly's decompiled source.</summary>
public static class AssemblySymbolInputResolver
{
    public static async Task<AssemblySymbolInputResolution> ResolveAsync(
        AssemblyNavigationSessionScope scope,
        string identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var normalized = InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (InputNormalizer.HasOpaqueHandoffPrefix(normalized)
            || normalized.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(new ResultError(
                NavigationErrorCodes.InvalidHandoff,
                "Opaque handoffs must be resolved by the assembly handoff resolver.",
                "Use the h: value returned for this target, or provide a raw declaration ID, position, or name."));
        }

        var resolved = await SourceSymbolResolver.ResolveRawWithoutHandoffsAsync(
            scope.Solution,
            normalized,
            cancellationToken).ConfigureAwait(false);
        if (resolved.Symbol is { } symbol)
        {
            return FromOwnedSymbol(scope, symbol, resolved.Candidates.FirstOrDefault());
        }

        if (resolved.Candidates.Count == 0)
            return Failure(resolved.Error ?? new ResultError(NavigationErrorCodes.SymbolNotFound, "No assembly source declaration matched the identifier."));

        var candidates = new List<AssemblySymbolInputCandidate>();
        foreach (var candidateSymbol in resolved.CandidateSymbols ?? Array.Empty<ISymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOwnedSourceSymbol(scope, candidateSymbol)) continue;
            var sourceCandidate = SourceSymbolResolver.DescribeCandidate(candidateSymbol, scope.Solution);
            var assemblyCandidate = sourceCandidate is null ? null : CreateCandidate(scope, candidateSymbol, sourceCandidate);
            if (assemblyCandidate is not null) candidates.Add(assemblyCandidate);
        }

        candidates = candidates
            .DistinctBy(candidate => candidate.HandoffId, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Line)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            return new(candidate.Symbol, candidate.HandoffId, [candidate], null);
        }
        if (candidates.Count > 1)
        {
            var choices = string.Join("; ", candidates.Select(candidate =>
                $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [targetPath: '{candidate.OwnerTargetPath}', handoffId: `{candidate.HandoffId}`]"));
            return new AssemblySymbolInputResolution(null, null, candidates, new ResultError(
                NavigationErrorCodes.AmbiguousSymbol,
                $"'{identifier}' matches multiple declarations in this assembly. Select a candidate by its owner targetPath and handoffId. {choices}"));
        }

        return Failure(new ResultError(
            NavigationErrorCodes.SymbolNotFound,
            $"'{identifier}' matched source declarations, but none could be proven to belong to this assembly's decompiled source."));
    }

    public static AnalysisSymbolIdentity CreateIdentity(AssemblyNavigationSessionScope scope) =>
        AnalysisSymbolIdentity.ForAssembly(
            scope.Context.Origin.CanonicalPath,
            scope.Context.Origin.ContentHash,
            scope.Context.Generation,
            scope.Context.ReferenceSnapshotHash);

    private static AssemblySymbolInputResolution FromOwnedSymbol(
        AssemblyNavigationSessionScope scope,
        ISymbol symbol,
        SymbolResolutionCandidate? sourceCandidate)
    {
        if (!IsOwnedSourceSymbol(scope, symbol))
            return Failure(new ResultError(
                NavigationErrorCodes.SymbolNotFound,
                "The identifier did not resolve to a declaration in this assembly's decompiled source."));

        var candidate = CreateCandidate(scope, symbol, sourceCandidate);
        if (candidate is null)
            return Failure(new ResultError(
                NavigationErrorCodes.InvalidArgument,
                "The selected declaration has no canonical assembly handoff identity."));
        return new(symbol, candidate.HandoffId, [candidate], null);
    }

    internal static AssemblySymbolInputCandidate? CreateCandidate(
        AssemblyNavigationSessionScope scope,
        ISymbol symbol,
        SymbolResolutionCandidate? sourceCandidate)
    {
        if (sourceCandidate is null) return null;
        var identity = CreateIdentity(scope);
        var internalHandoff = identity.FormatHandoff(symbol);
        if (string.IsNullOrWhiteSpace(internalHandoff)) return null;
        var handoff = HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalHandoff);
        return new AssemblySymbolInputCandidate(
            symbol,
            handoff,
            scope.Context.Origin.CanonicalPath,
            sourceCandidate.Name,
            sourceCandidate.Kind,
            sourceCandidate.Signature,
            sourceCandidate.FilePath,
            sourceCandidate.Line,
            sourceCandidate.EndLine,
            sourceCandidate.ProjectName,
            sourceCandidate.DocCommentId);
    }

    internal static bool IsOwnedSourceSymbol(AssemblyNavigationSessionScope scope, ISymbol symbol)
    {
        if (!symbol.Locations.Any(location => location.IsInSource
            && location.SourceTree is not null
            && scope.Solution.GetDocument(location.SourceTree) is not null)) return false;
        var declarationId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (string.IsNullOrWhiteSpace(declarationId)) return false;
        var owned = DocumentationCommentId.GetSymbolsForDeclarationId(declarationId, scope.Context.Compilation)
            .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, scope.Context.Assembly)
                && candidate.Locations.Any(location => location.IsInSource && location.SourceTree is not null
                    && scope.Solution.GetDocument(location.SourceTree) is not null))
            .Distinct(SymbolEqualityComparer.Default)
            .Take(2)
            .ToArray();
        return owned.Length == 1 && SymbolEqualityComparer.Default.Equals(owned[0], symbol);
    }

    private static AssemblySymbolInputResolution Failure(ResultError error) =>
        new(null, null, Array.Empty<AssemblySymbolInputCandidate>(), error);
}

public sealed record AssemblySymbolInputCandidate(
    ISymbol Symbol,
    string HandoffId,
    string OwnerTargetPath,
    string Name,
    string Kind,
    string Signature,
    string FilePath,
    int Line,
    int EndLine,
    string ProjectName,
    string? DocCommentId);

public sealed record AssemblySymbolInputResolution(
    ISymbol? Symbol,
    string? HandoffId,
    IReadOnlyList<AssemblySymbolInputCandidate> Candidates,
    ResultError? Error)
{
    public bool IsSuccess => Symbol is not null && HandoffId is not null && Error is null;
}
