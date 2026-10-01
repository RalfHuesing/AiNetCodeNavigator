#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
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
        string? expectedTargetPath = null)
    {
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
