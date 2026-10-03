#nullable enable

using System;
using System.Linq;
using AiNetCodeNavigator.Core.Models;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Assemblies;

/// <summary>Creates and resolves assembly references against one already leased owner scope.</summary>
public static class ExactAssemblySymbolResolver
{
    public static Result<StableSymbolReference.Assembly> CreateReference(AssemblyNavigationSessionScope scope, ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(symbol);

        var declaration = Normalize(symbol);
        if (declaration.IsImplicitlyDeclared || declaration is IMethodSymbol { MethodKind: MethodKind.LocalFunction })
        {
            return Result<StableSymbolReference.Assembly>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "Implicit declarations and local functions do not have supported stable declaration references.",
                "Use the declaration's raw assembly location because Roslyn does not provide a stable reference for it.");
        }

        if (!SymbolEqualityComparer.Default.Equals(declaration.ContainingAssembly, scope.Context.Assembly))
        {
            return Result<StableSymbolReference.Assembly>.Failure(
                NavigationErrorCodes.TargetMismatch,
                "The symbol is not owned by the selected assembly context.",
                "Open the symbol's intended assembly owner using its discovery result's ownerTargetPath, then rediscover and use that owner's reference.");
        }

        var declarationId = DocumentationCommentId.CreateDeclarationId(declaration);
        if (!StableSymbolReferenceCodec.IsCanonicalDeclarationId(declarationId ?? string.Empty))
        {
            return Result<StableSymbolReference.Assembly>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "Roslyn did not produce a supported declaration ID for this assembly symbol.",
                "Use the declaration's raw assembly location because it has no stable declaration ID.");
        }

        var reference = new StableSymbolReference.Assembly(GetOwnerSimpleName(scope), declarationId!);
        if (!StableSymbolReferenceCodec.TryFormatCanonical(reference, out _, out var codecError))
        {
            return Result<StableSymbolReference.Assembly>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                codecError?.Message ?? "The assembly declaration could not be encoded as a canonical reference.",
                "Rediscover the declaration in the selected assembly and use the reference it returns.");
        }

        var resolved = Resolve(scope, reference);
        if (!resolved.IsSuccess)
        {
            return Result<StableSymbolReference.Assembly>.Failure(resolved.Error!);
        }
        if (!SymbolEqualityComparer.Default.Equals(declaration, resolved.Value))
        {
            return Result<StableSymbolReference.Assembly>.Failure(
                NavigationErrorCodes.UnsupportedIdentifier,
                "Use the raw assembly location to select a declaration with a unique stable ID.",
                "Correct the exact assembly owner and declaration ID so the symbol round-trips uniquely, then rediscover it. If uniqueness cannot be established, navigate independently using the raw assembly location.");
        }

        return Result<StableSymbolReference.Assembly>.Success(reference);
    }

    public static Result<ISymbol> Resolve(AssemblyNavigationSessionScope scope, StableSymbolReference.Assembly reference)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(reference);
        if (!StableSymbolReferenceCodec.TryFormatCanonical(reference, out _, out var codecError))
        {
            return Result<ISymbol>.Failure(codecError ?? new ResultError(
                NavigationErrorCodes.InvalidSymbolReference,
                "The typed assembly reference is malformed or noncanonical."));
        }

        var actualName = GetOwnerSimpleName(scope);
        if (!string.Equals(reference.SimpleName, actualName, StringComparison.Ordinal))
        {
            return Result<ISymbol>.Failure(
                NavigationErrorCodes.TargetMismatch,
                $"The assembly reference names '{reference.SimpleName}' but the selected owner metadata names '{actualName}'.",
                "Reopen the original ownerTargetPath returned by discovery and resolve with that owner's scope. If that path is unavailable, rediscover on the intended binary and use its returned ownerTargetPath and reference; do not select by simple name alone.");
        }

        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(reference.DeclarationId, scope.Context.Compilation)
            .Select(Normalize)
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, scope.Context.Assembly)
                && string.Equals(DocumentationCommentId.CreateDeclarationId(symbol), reference.DeclarationId, StringComparison.Ordinal))
            .Distinct(SymbolEqualityComparer.Default)
            .ToArray();

        return symbols.Length switch
        {
            1 => Result<ISymbol>.Success(symbols[0]),
            0 => Result<ISymbol>.Failure(
                NavigationErrorCodes.SymbolNotFound,
                "The exact declaration ID does not resolve in the selected assembly owner.",
                "Rediscover the declaration in this assembly and use its returned reference; verify the declaration signature."),
            _ => Result<ISymbol>.Failure(
                NavigationErrorCodes.AmbiguousSymbol,
                "The declaration ID resolves to multiple symbols in the selected assembly owner.",
                "A stable reference requires a unique exact assembly owner and declaration ID. If uniqueness cannot be established, navigate independently using the raw assembly location."),
        };
    }

    private static ISymbol Normalize(ISymbol symbol) => symbol is IMethodSymbol { ReducedFrom: { } reduced }
        ? reduced.OriginalDefinition
        : symbol.OriginalDefinition;

    private static string GetOwnerSimpleName(AssemblyNavigationSessionScope scope)
    {
        var metadataName = scope.Context.Identity?.Name;
        return !string.IsNullOrWhiteSpace(metadataName)
            ? metadataName
            : scope.Context.Assembly.Identity.Name;
    }
}
