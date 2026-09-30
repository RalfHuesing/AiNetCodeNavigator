#nullable enable

using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

internal static class SourceHandoffFormatter
{
    internal static string? Format(ISymbol? symbol, Solution solution, AnalysisSymbolIdentity? identity)
    {
        if (symbol is null || identity is null)
        {
            return null;
        }

        var internalId = identity.FormatHandoff(symbol, solution);
        return internalId is null
            ? null
            : HandoffHandleRegistry.Default.GetOpaqueHandleForOutputOrThrow(internalId);
    }
}
