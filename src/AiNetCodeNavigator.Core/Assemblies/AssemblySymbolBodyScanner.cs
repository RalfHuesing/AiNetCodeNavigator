#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Common;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Returns decompiled declaration text for an inspect_assembly handoff.</summary>
public static class AssemblySymbolBodyScanner
{
    public static async Task<SymbolBodyResolutionResult> GetAsync(
        string handoff,
        int maxBodyLines = 100,
        int startLine = 1,
        CancellationToken cancellationToken = default,
        string? expectedTargetPath = null,
        AssemblyNavigationSessionScope? pinnedScope = null)
    {
        var normalizedIdentifier = InputNormalizer.NormalizeSymbolIdentifier(handoff);
        if (pinnedScope is not null)
        {
            if (expectedTargetPath is not null
                && (!SymbolHandoffToken.TryNormalizeTargetPath(expectedTargetPath, out var pinnedExpectedPath)
                    || !SymbolHandoffToken.TryNormalizeTargetPath(pinnedScope.Context.Origin.CanonicalPath, out var scopePath)
                    || !string.Equals(pinnedExpectedPath, scopePath,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            {
                return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(),
                    new ResultError(NavigationErrorCodes.TargetMismatch, "The pinned assembly scope belongs to a different target."));
            }
        }
        if ((expectedTargetPath is not null || pinnedScope is not null)
            && !InputNormalizer.HasOpaqueHandoffPrefix(normalizedIdentifier)
            && !normalizedIdentifier.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
        {
            if (pinnedScope is not null)
            {
                var pinnedRaw = await AssemblySymbolInputResolver.ResolveAsync(pinnedScope, normalizedIdentifier, cancellationToken).ConfigureAwait(false);
                if (!pinnedRaw.IsSuccess)
                {
                    var candidates = pinnedRaw.Candidates.Select(candidate => new SymbolResolutionCandidate(
                        candidate.Name, candidate.Kind, candidate.Signature, candidate.FilePath, candidate.Line,
                        candidate.EndLine, candidate.ProjectName, candidate.DocCommentId, candidate.HandoffId,
                        candidate.OwnerTargetPath)).ToArray();
                    return new SymbolBodyResolutionResult(null, candidates, pinnedRaw.Error);
                }

                var pinnedRawBody = SourceSymbolBodyResolver.Resolve(pinnedRaw.Symbol!, maxBodyLines, startLine, handoffId: pinnedRaw.HandoffId);
                return new SymbolBodyResolutionResult(pinnedRawBody with { ContentMode = "decompiled", HandoffId = pinnedRaw.HandoffId },
                    Array.Empty<SymbolResolutionCandidate>(), null);
            }

            var openedScope = await AssemblyNavigationSessionScope.OpenAsync(expectedTargetPath, cancellationToken).ConfigureAwait(false);
            if (!openedScope.IsSuccess)
                return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(), openedScope.Error);

            await using var scope = openedScope.Value!;
            var raw = await AssemblySymbolInputResolver.ResolveAsync(scope, normalizedIdentifier, cancellationToken).ConfigureAwait(false);
            if (!raw.IsSuccess)
            {
                var candidates = raw.Candidates.Select(candidate => new SymbolResolutionCandidate(
                    candidate.Name,
                    candidate.Kind,
                    candidate.Signature,
                    candidate.FilePath,
                    candidate.Line,
                    candidate.EndLine,
                    candidate.ProjectName,
                    candidate.DocCommentId,
                    candidate.HandoffId,
                    candidate.OwnerTargetPath)).ToArray();
                return new SymbolBodyResolutionResult(null, candidates, raw.Error);
            }

            var rawBody = SourceSymbolBodyResolver.Resolve(
                raw.Symbol!,
                maxBodyLines,
                startLine,
                handoffId: raw.HandoffId);
            return new SymbolBodyResolutionResult(
                rawBody with { ContentMode = "decompiled", HandoffId = raw.HandoffId },
                Array.Empty<SymbolResolutionCandidate>(),
                null);
        }

        if (pinnedScope is not null)
        {
            var pinned = AssemblySymbolHandoffResolver.ResolveWithinScope(handoff, pinnedScope);
            if (!pinned.IsSuccess)
                return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(), pinned.Error);
            var pinnedBody = SourceSymbolBodyResolver.Resolve(pinned.Value!, maxBodyLines, startLine, handoffId: handoff);
            return new SymbolBodyResolutionResult(pinnedBody with { ContentMode = "decompiled", HandoffId = handoff },
                Array.Empty<SymbolResolutionCandidate>(), null);
        }

        var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(handoff, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(), resolved.Error);
        }

        await using var access = resolved.Value!;
        if (expectedTargetPath is not null
            && (!SymbolHandoffToken.TryNormalizeTargetPath(expectedTargetPath, out var expectedPath)
                || !SymbolHandoffToken.TryNormalizeTargetPath(access.Origin.CanonicalPath, out var ownerPath)
                || !string.Equals(expectedPath, ownerPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
        {
            return new SymbolBodyResolutionResult(
                null,
                Array.Empty<SymbolResolutionCandidate>(),
                new ResultError(
                    NavigationErrorCodes.TargetMismatch,
                    "The assembly handoff belongs to a different target assembly.",
                    $"Repeat get_symbol_body with the targetPath returned by find_symbol: '{access.Origin.CanonicalPath}'."));
        }

        var body = SourceSymbolBodyResolver.Resolve(
            access.Symbol,
            maxBodyLines,
            startLine,
            handoffId: handoff);
        return new SymbolBodyResolutionResult(
            body with { ContentMode = "decompiled", HandoffId = handoff },
            Array.Empty<SymbolResolutionCandidate>(),
            null);
    }
}
