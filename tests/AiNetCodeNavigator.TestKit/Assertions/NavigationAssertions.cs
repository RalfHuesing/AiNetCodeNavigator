#nullable enable

namespace AiNetCodeNavigator.TestKit.Assertions;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using AiNetCodeNavigator.Core.Symbols;
using Xunit;

/// <summary>
/// Semantic assertions for navigation results, symbols, and MCP data.
/// </summary>
public static class NavigationAssertions
{
    /// <summary>
    /// Checks whether a handoff identifier follows the product's handle alphabet (for example, "h:gwtQ").
    /// </summary>
    public static void AssertValidHandoffId(string? handoffId)
    {
        Assert.True(HandoffCounterAlphabet.IsValidHandle(handoffId), $"'{handoffId}' is not a valid handoff ID.");
    }

    /// <summary>
    /// Checks that a symbol exists and has the expected name.
    /// </summary>
    public static void AssertSymbolName(ISymbol? symbol, string expectedName)
    {
        Assert.NotNull(symbol);
        Assert.Equal(expectedName, symbol.Name);
    }

    /// <summary>
    /// Checks that a line range is valid and has at least the requested number of lines.
    /// </summary>
    public static void AssertValidLineRange(int startLine, int endLine, int minimumLines = 1)
    {
        Assert.True(startLine >= 1, $"Startzeile {startLine} muss >= 1 sein.");
        Assert.True(endLine >= startLine, $"Endzeile {endLine} muss >= Startzeile {startLine} sein.");
        var lineCount = endLine - startLine + 1;
        Assert.True(lineCount >= minimumLines, $"Zeilenzahl {lineCount} muss mindestens {minimumLines} betragen.");
    }

    /// <summary>
    /// Checks that a sequence of strings contains the requested substring.
    /// </summary>
    public static void AssertContainsPattern(IEnumerable<string> items, string substring)
    {
        Assert.Contains(items, item => item.Contains(substring, StringComparison.Ordinal));
    }
}
