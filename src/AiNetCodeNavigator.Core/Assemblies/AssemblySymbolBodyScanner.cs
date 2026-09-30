#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Returns decompiled declaration text for an inspect_assembly handoff.</summary>
public static class AssemblySymbolBodyScanner
{
    public static async Task<SymbolBodyResolutionResult> GetAsync(
        string handoff,
        int maxBodyLines = 100,
        int startLine = 1,
        CancellationToken cancellationToken = default)
    {
        var resolved = await AssemblySymbolHandoffResolver.ResolveAsync(handoff, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return new SymbolBodyResolutionResult(null, Array.Empty<SymbolResolutionCandidate>(), resolved.Error);
        }

        await using var access = resolved.Value!;
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
