#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Extracts the source code (body/definition) of a C# symbol from the Roslyn AST.
/// Supports line pagination (startLine, maxBodyLines) and batch extraction.
/// </summary>
public static class SourceSymbolBodyResolver
{
    /// <summary>Resolves a source identifier before extracting its body; ambiguous names return selectable candidates.</summary>
    public static async Task<SymbolBodyResolutionResult> ResolveAsync(
        Solution solution,
        string symbolIdentifier,
        int maxBodyLines,
        int startLine = 1,
        CancellationToken cancellationToken = default)
    {
        var resolution = await SourceSymbolResolver.ResolveAsync(solution, symbolIdentifier, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return new SymbolBodyResolutionResult(null, resolution.Candidates, resolution.Error);
        }

        var candidate = resolution.Candidates.FirstOrDefault();
        var body = Resolve(
            resolution.Symbol!,
            maxBodyLines,
            startLine,
            candidate?.HandoffId,
            solution: solution);
        return new SymbolBodyResolutionResult(body, resolution.Candidates, null);
    }

    internal static async Task<SymbolBodyResolutionResult> ResolveAsync(
        Solution solution,
        string symbolIdentifier,
        int maxBodyLines,
        int startLine,
        AnalysisSymbolIdentity? suppliedIdentity,
        SourceIdentityRequest currentIdentityRequest,
        CancellationToken cancellationToken = default)
    {
        var resolution = await SourceSymbolResolver.ResolveAsync(solution, symbolIdentifier, suppliedIdentity,
            currentIdentityRequest, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
            return new SymbolBodyResolutionResult(null, resolution.Candidates, resolution.Error);

        var candidate = resolution.Candidates.FirstOrDefault();
        var body = Resolve(resolution.Symbol!, maxBodyLines, startLine, candidate?.HandoffId,
            solution: solution);
        return new SymbolBodyResolutionResult(body, resolution.Candidates, null);
    }

    /// <summary>
    /// Extracts the symbol declaration from source syntax and returns the requested one-based line window.
    /// Values below one for <paramref name="maxBodyLines"/> or <paramref name="startLine"/> are normalized to one.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="symbol"/> is null.</exception>
    public static SymbolBodyResult Resolve(
        ISymbol symbol,
        int maxBodyLines,
        int startLine = 1,
        string? handoffId = null,
        Solution? solution = null)
        => ResolveCore(symbol, maxBodyLines, startLine, handoffId, solution,
            symbol is null ? null : GetBodySyntaxReference(symbol));

    internal static SymbolBodyResult ResolveWithDeclaration(
        ISymbol symbol,
        SyntaxReference declarationReference,
        int maxBodyLines,
        int startLine = 1,
        string? handoffId = null,
        Solution? solution = null)
    {
        ArgumentNullException.ThrowIfNull(declarationReference);
        return ResolveCore(symbol, maxBodyLines, startLine, handoffId, solution, declarationReference);
    }

    private static SymbolBodyResult ResolveCore(
        ISymbol symbol,
        int maxBodyLines,
        int startLine,
        string? handoffId,
        Solution? solution,
        SyntaxReference? declarationReference)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var hasSyntax = declarationReference is not null;
        var unavailable = HasUnavailableBody(symbol, hasSyntax);
        var hint = GetHint(symbol, hasSyntax, unavailable);
        var docCommentId = symbol.GetDocumentationCommentId();
        var (body, totalLines, displayedStart, displayedEnd, hasMore) = Extract(symbol, maxBodyLines, startLine,
            declarationReference);

        return new SymbolBodyResult(
            Body: body,
            Availability: unavailable ? "unavailable" : "available",
            ContentMode: "source",
            Hint: hint,
            TotalLines: totalLines,
            DisplayedStart: displayedStart,
            DisplayedEnd: displayedEnd,
            HasMore: hasMore,
            DocCommentId: docCommentId,
            HandoffId: handoffId);
    }

    /// <summary>
    /// Extracts the same line window for each input symbol, preserving input order.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="symbols"/> is null.</exception>
    public static BatchSymbolBodyResult ResolveBatch(
        IEnumerable<ISymbol> symbols,
        int maxBodyLines,
        int startLine = 1)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var results = symbols
            .Select(s => Resolve(s, maxBodyLines, startLine))
            .ToList();

        return new BatchSymbolBodyResult(results);
    }

    private static bool HasUnavailableBody(ISymbol symbol, bool hasSyntax) =>
        !hasSyntax
        || symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface }
        || symbol switch
        {
            IMethodSymbol method => method.IsAbstract
                || HasExternModifier(method)
                || (method.IsPartialDefinition && method.PartialImplementationPart is null),
            IPropertySymbol property => HasNoBody(property),
            IEventSymbol eventSymbol => eventSymbol.AddMethod?.IsAbstract == true
                || eventSymbol.RemoveMethod?.IsAbstract == true,
            _ => false,
        };

    private static string? GetHint(ISymbol symbol, bool hasSyntax, bool unavailable)
    {
        if (!hasSyntax) return "No source syntax is available for the symbol.";
        if (!unavailable) return null;
        if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface }
            || symbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return "Interfaces do not provide an executable body for this symbol.";
        }

        if (symbol is IMethodSymbol { IsPartialDefinition: true, PartialImplementationPart: null })
        {
            return "The partial method declaration has no implementation.";
        }

        return "The symbol is abstract or extern and has no body.";
    }

    private static bool HasNoBody(IPropertySymbol property) =>
        property.GetMethod?.IsAbstract == true
        || property.SetMethod?.IsAbstract == true
        || HasExternModifier(property.GetMethod)
        || HasExternModifier(property.SetMethod);

    private static bool HasExternModifier(ISymbol? symbol) =>
        symbol is not null
        && GetBodySyntaxReference(symbol) is { } reference
        && reference.GetSyntax() is MemberDeclarationSyntax member
        && member.Modifiers.Any(SyntaxKind.ExternKeyword);

    private static SyntaxReference? GetBodySyntaxReference(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { PartialImplementationPart: { } implementation })
        {
            return implementation.DeclaringSyntaxReferences.FirstOrDefault();
        }

        return symbol.DeclaringSyntaxReferences.FirstOrDefault();
    }

    private static (string Body, int TotalLines, int DisplayedStart, int DisplayedEnd, bool HasMore) Extract(
        ISymbol symbol,
        int maxBodyLines,
        int startLine,
        SyntaxReference? declarationOverride = null)
    {
        var normalizedMax = Math.Max(1, maxBodyLines);
        var normalizedStart = Math.Max(1, startLine);
        var declaringReference = declarationOverride ?? GetBodySyntaxReference(symbol);
        if (declaringReference is null)
        {
            return ($"// No source syntax available for '{symbol.ToDisplayString()}'.", 0, 1, 0, false);
        }

        var text = declaringReference.GetSyntax().ToFullString();
        var lines = text.Split('\n');
        var totalLines = lines.Length;

        if (normalizedStart > totalLines)
        {
            return (
                $"// startLine {normalizedStart} is outside the symbol ({totalLines} lines total).",
                totalLines,
                normalizedStart,
                normalizedStart,
                false);
        }

        var startIndex = normalizedStart - 1;
        var count = Math.Min(normalizedMax, totalLines - startIndex);
        var selectedLines = lines.Skip(startIndex).Take(count).ToArray();
        var displayedEnd = normalizedStart + count - 1;
        var hasMore = displayedEnd < totalLines;

        var body = string.Join("\n", selectedLines).TrimEnd();
        if (hasMore)
        {
            body += $"\n// ... truncated, {totalLines} lines total (showing {normalizedStart}-{displayedEnd}), adjust startLine/maxBodyLines for more";
        }

        return (body, totalLines, normalizedStart, displayedEnd, hasMore);
    }
}
