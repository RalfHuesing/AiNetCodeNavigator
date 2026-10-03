#nullable enable

using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

internal static class StableSourceReferenceFormatter
{
    internal static string? Format(ISymbol? symbol, Solution solution, AnalysisSymbolIdentity? identity)
    {
        if (symbol is null || identity is null)
        {
            return null;
        }

        return identity.FormatHandoff(symbol, solution);
    }
}
