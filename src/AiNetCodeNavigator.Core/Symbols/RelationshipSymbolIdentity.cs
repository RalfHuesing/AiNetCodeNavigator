#nullable enable

using System.Linq;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Symbols;

internal static class RelationshipSymbolIdentity
{
    public static string GetStableId(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction)
        {
            var container = localFunction.ContainingSymbol;
            while (container is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
            {
                container = container.ContainingSymbol;
            }

            var location = localFunction.Locations.FirstOrDefault(candidate => candidate.IsInSource);
            if (location is not null)
            {
                var start = location.GetLineSpan().StartLinePosition;
                return $"{GetStableId(container!)}#lf:{localFunction.Name}@{start.Line + 1}:{start.Character + 1}";
            }
        }

        return DocumentationCommentId.CreateDeclarationId(symbol)
            ?? symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
