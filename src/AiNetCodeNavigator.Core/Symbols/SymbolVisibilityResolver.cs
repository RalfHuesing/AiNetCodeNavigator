#nullable enable

using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Helper zur Ermittlung der Sichtbarkeit eines Roslyn-Symbols.
/// </summary>
public static class SymbolVisibilityResolver
{
    public static string ResolveVisibility(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private",
        };

    public static string ResolveVisibility(ISymbol symbol) =>
        ResolveVisibility(symbol.DeclaredAccessibility);
}
