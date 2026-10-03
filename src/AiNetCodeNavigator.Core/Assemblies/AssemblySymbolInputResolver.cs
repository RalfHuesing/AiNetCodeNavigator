#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
        if (TryRouteIdentifier(identifier, normalized, out var reference, out var referenceError))
        {
            if (referenceError is not null) return Failure(referenceError.Value);
            if (reference is not StableSymbolReference.Assembly assemblyReference)
                return Failure(new ResultError(
                    NavigationErrorCodes.TargetMismatch,
                    "A source reference cannot be resolved in an assembly target.",
                    "Open the returned source solution target and use the same source reference there."));
            var exact = ExactAssemblySymbolResolver.Resolve(scope, assemblyReference);
            if (!exact.IsSuccess) return Failure(exact.Error!.Value);
            return FromOwnedSymbol(scope, exact.Value!, SourceSymbolResolver.DescribeCandidate(exact.Value!, scope.Solution));
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
            .DistinctBy(candidate => candidate.HandoffId ?? $"{candidate.OwnerTargetPath}|{candidate.FilePath}|{candidate.Line}|{candidate.Signature}", StringComparer.Ordinal)
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
            var choices = string.Join("; ", candidates.Select(candidate => candidate.HandoffId is { Length: > 0 } reference
                ? $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [reference: `{reference}`, ownerTargetPath: '{candidate.OwnerTargetPath}']"
                : $"{candidate.Signature} at {candidate.FilePath}:{candidate.Line} [raw location: `{candidate.FilePath}:{candidate.Line}`, ownerTargetPath: '{candidate.OwnerTargetPath}', no stable reference available]"));
            var hasRawOnlyCandidates = candidates.Any(static candidate => string.IsNullOrWhiteSpace(candidate.HandoffId));
            return new AssemblySymbolInputResolution(null, null, candidates, new ResultError(
                NavigationErrorCodes.AmbiguousSymbol,
                $"'{identifier}' matches multiple declarations in this assembly. {choices}",
                hasRawOnlyCandidates
                    ? "Select a candidate with its exact asm: reference and ownerTargetPath when available. For a declaration without a stable reference, repeat the raw file:line location shown above with the same ownerTargetPath."
                    : "Select a candidate with its exact asm: reference and ownerTargetPath, then open that owner target for follow-up tools."));
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

    public static bool TryRouteIdentifier(string identifier, string? discoveryNormalizedIdentifier,
        out StableSymbolReference? reference, out ResultError? error) =>
        StableSymbolReferenceCodec.TryParseReferenceInput(identifier, discoveryNormalizedIdentifier,
            out reference, out error);

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
        return new(symbol, candidate?.HandoffId, candidate is null ? Array.Empty<AssemblySymbolInputCandidate>() : [candidate], null);
    }

    internal static AssemblySymbolInputCandidate? CreateCandidate(
        AssemblyNavigationSessionScope scope,
        ISymbol symbol,
        SymbolResolutionCandidate? sourceCandidate)
    {
        if (sourceCandidate is null) return null;
        var reference = ExactAssemblySymbolResolver.CreateReference(scope, symbol);
        var handoff = reference.IsSuccess ? StableSymbolReferenceCodec.Format(reference.Value!) : null;
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
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, scope.Context.Assembly)) return false;
        var decompiledRoot = scope.Context.DecompiledProjectPaths?.DecompiledSourceRoot;
        var ownerTrees = scope.Context.Compilation.SyntaxTrees.ToHashSet();
        return symbol.DeclaringSyntaxReferences.Any(reference => ownerTrees.Contains(reference.SyntaxTree)
            && scope.Solution.GetDocument(reference.SyntaxTree)?.FilePath is { } filePath
            && IsWithin(decompiledRoot, filePath));
    }

    private static AssemblySymbolInputResolution Failure(ResultError error) =>
        new(null, null, Array.Empty<AssemblySymbolInputCandidate>(), error);

    private static bool IsWithin(string? rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return false;
        try
        {
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(candidatePath);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison)
                || string.Equals(candidate, root, comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}

public sealed record AssemblySymbolInputCandidate(
    ISymbol Symbol,
    string? HandoffId,
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
    public bool IsSuccess => Symbol is not null && Error is null;
}
