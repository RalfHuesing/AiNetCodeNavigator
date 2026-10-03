#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Returns a decompiled declaration window from the explicitly selected assembly owner.</summary>
public static class AssemblySymbolBodyScanner
{
    public static async Task<SymbolBodyResolutionResult> GetAsync(
        string identifier,
        int maxBodyLines = 100,
        int startLine = 1,
        CancellationToken cancellationToken = default,
        string? expectedTargetPath = null,
        AssemblyNavigationSessionScope? pinnedScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        var normalized = AiNetCodeNavigator.Core.Common.InputNormalizer.NormalizeSymbolIdentifier(identifier);
        if (AssemblySymbolInputResolver.TryRouteIdentifier(identifier, normalized, out var reference, out var routeError))
        {
            if (routeError is not null) return Failed(routeError.Value);
            if (reference is not StableSymbolReference.Assembly)
                return Failed(new ResultError(NavigationErrorCodes.TargetMismatch,
                    "A source reference cannot be resolved in an assembly target.",
                    "Open the source solution target and use the src: reference there."));
        }
        if (pinnedScope is not null && expectedTargetPath is not null
            && !SamePath(expectedTargetPath, pinnedScope.Context.Origin.CanonicalPath))
            return Failed(new ResultError(NavigationErrorCodes.TargetMismatch,
                "The pinned assembly scope belongs to a different target.",
                "Use the targetPath that owns this declaration reference."));

        if (pinnedScope is not null)
            return await ResolveInScopeAsync(pinnedScope, identifier, maxBodyLines, startLine, cancellationToken).ConfigureAwait(false);

        if (expectedTargetPath is null)
            return Failed(new ResultError(NavigationErrorCodes.InvalidArgument,
                "Assembly body lookup requires an explicit owner targetPath.",
                "Use the ownerTargetPath returned with the assembly reference."));

        var opened = await AssemblyNavigationSessionScope.OpenAsync(expectedTargetPath, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess) return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(), opened.Error);
        await using var scope = opened.Value!;
        return await ResolveInScopeAsync(scope, identifier, maxBodyLines, startLine, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SymbolBodyResolutionResult> ResolveInScopeAsync(
        AssemblyNavigationSessionScope scope,
        string identifier,
        int maxBodyLines,
        int startLine,
        CancellationToken cancellationToken)
    {
        var resolution = await AssemblySymbolInputResolver.ResolveAsync(scope, identifier, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            var candidates = resolution.Candidates.Select(candidate => new SymbolResolutionCandidate(
                candidate.Name, candidate.Kind, candidate.Signature, candidate.FilePath, candidate.Line,
                candidate.EndLine, candidate.ProjectName, candidate.DocCommentId, candidate.HandoffId,
                candidate.OwnerTargetPath)).ToArray();
            return new SymbolBodyResolutionResult(null, candidates, resolution.Error);
        }

        var body = SourceSymbolBodyResolver.Resolve(resolution.Symbol!, maxBodyLines, startLine,
            handoffId: resolution.HandoffId);
        return new SymbolBodyResolutionResult(body with
        {
            ContentMode = "decompiled",
            HandoffId = resolution.HandoffId,
        }, Array.Empty<SymbolResolutionCandidate>(), null);
    }

    private static SymbolBodyResolutionResult Failed(ResultError error) =>
        new(null, Array.Empty<SymbolResolutionCandidate>(), error);

    private static bool SamePath(string left, string right) =>
        StableTargetPath.TryNormalizeTargetPath(left, out var leftPath)
        && StableTargetPath.TryNormalizeTargetPath(right, out var rightPath)
        && string.Equals(leftPath, rightPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
