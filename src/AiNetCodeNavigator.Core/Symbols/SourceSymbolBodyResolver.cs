#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiNetCodeNavigator.Core.Symbols;

/// <summary>
/// Extrahiert den Quellcode (Body/Definition) eines C#-Symbols aus dem Roslyn-AST.
/// Unterstützt Zeilen-Paginierung (startLine, maxBodyLines) und Batch-Extraktion.
/// </summary>
public static class SourceSymbolBodyResolver
{
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
        AnalysisSymbolIdentity? handoffIdentity = null,
        Solution? solution = null)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var hasSyntax = GetBodySyntaxReference(symbol) is not null;
        var unavailable = HasUnavailableBody(symbol, hasSyntax);
        var hint = GetHint(symbol, hasSyntax, unavailable);
        var docCommentId = symbol.GetDocumentationCommentId();
        handoffId ??= solution is null ? null : SourceHandoffFormatter.Format(symbol, solution, handoffIdentity);

        var (body, totalLines, displayedStart, displayedEnd, hasMore) = Extract(symbol, maxBodyLines, startLine);

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
        if (!hasSyntax) return "Für das Symbol ist kein Quell-Syntax verfügbar.";
        if (!unavailable) return null;
        if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface }
            || symbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return "Interfaces stellen für dieses Symbol keinen ausführbaren Body bereit.";
        }

        if (symbol is IMethodSymbol { IsPartialDefinition: true, PartialImplementationPart: null })
        {
            return "Die partielle Methodendeklaration besitzt keine Implementierung.";
        }

        return "Das Symbol ist abstract oder extern und besitzt keinen Body.";
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
        int startLine)
    {
        var normalizedMax = Math.Max(1, maxBodyLines);
        var normalizedStart = Math.Max(1, startLine);
        var declaringReference = GetBodySyntaxReference(symbol);
        if (declaringReference is null)
        {
            return ($"// Kein Quell-Syntax verfügbar für '{symbol.ToDisplayString()}'.", 0, 1, 0, false);
        }

        var text = declaringReference.GetSyntax().ToFullString();
        var lines = text.Split('\n');
        var totalLines = lines.Length;

        if (normalizedStart > totalLines)
        {
            return (
                $"// startLine {normalizedStart} liegt außerhalb des Symbols (gesamt {totalLines} Zeilen).",
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
            body += $"\n// ... truncated, gesamt {totalLines} Zeilen (angezeigt {normalizedStart}-{displayedEnd}), startLine/maxBodyLines anpassen für mehr";
        }

        return (body, totalLines, normalizedStart, displayedEnd, hasMore);
    }
}
