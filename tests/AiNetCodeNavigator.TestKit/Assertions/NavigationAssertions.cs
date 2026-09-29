#nullable enable

namespace AiNetCodeNavigator.TestKit.Assertions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

/// <summary>
/// Semantische Assertions für Navigationsergebnisse, Symbole und MCP-Daten.
/// </summary>
public static partial class NavigationAssertions
{
    [GeneratedRegex(@"^h:[a-zA-Z0-9_-]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HandoffRegex();

    /// <summary>
    /// Prüft, ob ein Handoff-Token syntaktisch valide ist (z.B. "h:gwtQ").
    /// </summary>
    public static void AssertValidHandoffId(string? handoffId)
    {
        Assert.NotNull(handoffId);
        Assert.Matches(HandoffRegex(), handoffId);
    }

    /// <summary>
    /// Prüft, ob ein Symbol existiert und den erwarteten Namen trägt.
    /// </summary>
    public static void AssertSymbolName(ISymbol? symbol, string expectedName)
    {
        Assert.NotNull(symbol);
        Assert.Equal(expectedName, symbol.Name);
    }

    /// <summary>
    /// Prüft, ob ein Zeilenbereich plausibel ist.
    /// </summary>
    public static void AssertValidLineRange(int startLine, int endLine, int minimumLines = 1)
    {
        Assert.True(startLine >= 1, $"Startzeile {startLine} muss >= 1 sein.");
        Assert.True(endLine >= startLine, $"Endzeile {endLine} muss >= Startzeile {startLine} sein.");
        var lineCount = endLine - startLine + 1;
        Assert.True(lineCount >= minimumLines, $"Zeilenzahl {lineCount} muss mindestens {minimumLines} betragen.");
    }

    /// <summary>
    /// Prüft, ob eine Liste von Strings ein bestimmtes Teilmuster enthält.
    /// </summary>
    public static void AssertContainsPattern(IEnumerable<string> items, string substring)
    {
        Assert.Contains(items, item => item.Contains(substring, StringComparison.Ordinal));
    }
}
