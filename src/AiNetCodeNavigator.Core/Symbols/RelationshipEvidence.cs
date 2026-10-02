#nullable enable

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>Classifies the static evidence available for a source relationship.</summary>
public static class RelationshipEvidence
{
    public const string Call = "call";
    public const string MemberAccess = "memberAccess";
    public const string StaticVirtualOrInterfaceTarget = "staticVirtualOrInterfaceTarget";
    public const string PossibleTarget = "possibleTarget";
    public const string Unresolved = "unresolved";

    public static string Classify(SyntaxNode referenceNode, SemanticModel? semanticModel)
    {
        if (semanticModel is null)
        {
            return Unresolved;
        }

        var invocation = referenceNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(node => IsCalleeReference(node, referenceNode));
        if (invocation is not null)
        {
            var info = semanticModel.GetSymbolInfo(invocation);
            if (info.Symbol is null)
            {
                return info.CandidateSymbols.Length > 0 ? PossibleTarget : Unresolved;
            }
            return IsStaticallySelectedVirtualOrInterfaceMember(info.Symbol)
                ? StaticVirtualOrInterfaceTarget
                : Call;
        }

        var creation = referenceNode.AncestorsAndSelf().FirstOrDefault(node =>
            node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax);
        if (creation is not null)
        {
            var info = semanticModel.GetSymbolInfo(creation);
            var isConstructorReference = creation.Span.Equals(referenceNode.Span)
                || creation is ObjectCreationExpressionSyntax objectCreation
                    && objectCreation.Type.Span.Contains(referenceNode.Span);
            if (!isConstructorReference) return ClassifyMemberOrUnresolved(referenceNode, semanticModel);
            return info.Symbol is not null ? Call : info.CandidateSymbols.Length > 0 ? PossibleTarget : Unresolved;
        }

        return ClassifyMemberOrUnresolved(referenceNode, semanticModel);
    }

    public static bool IsStaticallySelectedVirtualOrInterfaceMember(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.ContainingType?.TypeKind == TypeKind.Interface
                || method.IsVirtual || method.IsAbstract || method.IsOverride,
            IPropertySymbol property => property.ContainingType?.TypeKind == TypeKind.Interface
                || property.IsVirtual || property.IsAbstract || property.IsOverride,
            _ => false,
        };
    }

    private static string ClassifyMemberOrUnresolved(SyntaxNode referenceNode, SemanticModel semanticModel)
    {
        var memberAccess = referenceNode.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess ?? referenceNode);
        return ClassifyMemberOrUnresolved(symbolInfo);
    }

    private static string ClassifyMemberOrUnresolved(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is null)
        {
            return symbolInfo.CandidateSymbols.Length > 0 ? PossibleTarget : Unresolved;
        }
        return IsStaticallySelectedVirtualOrInterfaceMember(symbolInfo.Symbol)
            ? StaticVirtualOrInterfaceTarget
            : MemberAccess;
    }

    private static bool IsCalleeReference(InvocationExpressionSyntax invocation, SyntaxNode referenceNode)
    {
        if (invocation.Span.Equals(referenceNode.Span)) return true;
        var expression = invocation.Expression;
        if (expression.Span.Equals(referenceNode.Span)) return true;
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Span.Contains(referenceNode.Span),
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Span.Contains(referenceNode.Span),
            IdentifierNameSyntax identifier => identifier.Span.Contains(referenceNode.Span),
            GenericNameSyntax generic => generic.Span.Contains(referenceNode.Span),
            _ => false,
        };
    }
}
